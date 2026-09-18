import { useCallback, useEffect, useRef, useState } from "react";
import { useSliceLoader } from "./dedupSliceLoader";
import { failure, isAbort, isAccessFailure } from "./queryState";
import { DedupClient } from "../dedup/dedupClient";
import { performAction, type DedupAction } from "../dedup/dedupActions";
import { emptyDedupView, type DedupJobHandle, type DedupView } from "../dedup/dedupJobState";
import { newerThan, sameJob, sliceIdentity, sliceKey } from "./dedupSlices";
import type { DedupExportReceipt, DedupFindingKind, DedupGroup } from "../dedup/dedupTypes";

const pollMilliseconds = 2_000;
const recheckPollMilliseconds = 1_500;

/**
 * State of one dedup workbench session for one library and one open section. It owns request sequencing
 * — start once, poll while the job is active, page by signed cursor, follow a recheck — and never
 * interprets a result itself: counts, freshness and incompleteness come from the server's own fields.
 *
 * The operation key is minted once per library and kept across retries so a resubmitted start is the
 * same request rather than a second job.
 */
export function useDedupJob(
  client: DedupClient,
  libraryId: string | null,
  section: DedupFindingKind,
  onExported?: (receipt: DedupExportReceipt) => void,
): DedupJobHandle {
  const [state, setState] = useState<DedupView>(emptyDedupView);
  const [revision, setRevision] = useState(0);
  /** The group whose members this page is showing. Null means the section listing is shown. */
  const [groupKey, setGroupKey] = useState<string | null>(null);
  /**
   * The report version this page is bound to, as the server named it. A recheck files a new version of
   * the same task, so the version — not the job — is what says whether the slice on screen is the
   * server's current one.
   */
  const [reportVersion, setReportVersion] = useState<string | null>(null);
  /**
   * The slice whose answer is on screen, and the version that answer is the server's answer at. Both have
   * to match the wanted slice for the page to be showing the server's current version. They are refs
   * because they record what a read did, and that record has to survive the extra mount React performs in
   * development — a state written there would be reset and the read repeated.
   */
  const readSlice = useRef<string | null>(null);
  const readVersion = useRef<string | null>(null);
  const query = useRef<AbortController | null>(null);
  const mutation = useRef<AbortController | null>(null);
  const current = useRef(libraryId);
  const live = useRef(state);
  live.current = state;
  // A read records the version that is current when its answer arrives, not the one from the render its
  // callback was created in, so the version is held in a ref and the callback stays stable.
  const liveVersion = useRef(reportVersion);
  liveVersion.current = reportVersion;
  const operationKey = useRef<string>(crypto.randomUUID());
  const activeLoad = useRef<string | null>(null);
  // The section and the group the reader has open right now. They are read by the recovery path after a
  // read ends rather than from that read's closure, because the reader may have moved on while it was on
  // the wire and it is what they moved to that has to be read, not what this read was for.
  const liveSection = useRef(section);
  liveSection.current = section;
  const liveGroup = useRef(groupKey);
  liveGroup.current = groupKey;
  /**
   * The slice the reader is asking for right now: the task, section and group this render is showing. It is
   * what makes "the reader came back to a section whose answer is still on screen" tellable from "the reader
   * never left": the answer on screen belongs to the slice that was asked for when it arrived, so coming
   * back is a different ask and has to be read again, while staying put is not an ask at all.
   */
  const liveWanted = useRef<string | null>(null);
  /** The slice a read is being made for right now, so coming back to it does not send a second request. */
  const reading = useRef<string | null>(null);
  /** Whether the read that just ended was aborted by the read that took its place on the wire. */
  const replaced = useRef(false);
  const exportedReceipt = useRef(onExported);
  exportedReceipt.current = onExported;
  useEffect(() => {
    current.current = libraryId;
    operationKey.current = crypto.randomUUID();
    activeLoad.current = null;
    readSlice.current = null;
    readVersion.current = null;
    setGroupKey(null);
    // The version belongs to the library that is open, so it is dropped with the rest of that library's
    // state: a version read for one library must never be exported against another.
    setReportVersion(null);
    mutation.current?.abort();
    query.current?.abort();

    // Switching library, or losing it, must not leave the previous library's findings on screen. No
    // analysis is requested here: the reader's own start is the only thing that begins one.
    setState(libraryId === null ? emptyDedupView : { ...emptyDedupView, status: "loading" });
    setRevision((value) => value + 1);
  }, [client, libraryId]);

  // The read controller, built here and given the refs it works through: it is the only place a read is
  // dispatched, so every rule about which read wins lives in one function.
  const load = useSliceLoader({
    client,
    libraryId,
    state: live,
    current,
    activeLoad,
    reading,
    replaced,
    liveVersion,
    readSlice,
    readVersion,
    liveSection,
    liveGroup,
    liveWanted,
    query,
    setState,
    setReportVersion,
  });

  // Reading a report and following its progress are the same effect: a succeeded job with a retained
  // report is read, and an active job is polled until it settles.
  useEffect(() => {
    if (libraryId === null) return;
    const job = live.current.job;
    if (job === null) return;
    let disposed = false;
    let timer: number | undefined;
    const active = job.state === "queued" || job.state === "leased";
    const step = async () => {
      if (disposed) return;
      if (active) {
        try {
          const next = await client.status(libraryId, job.task_id, new AbortController().signal);
          if (disposed || current.current !== libraryId) return;
          // An answer that says what is already on screen is not applied. Every status answer is a new
          // object once parsed, so writing it unconditionally would re-render, re-run this effect and read
          // again: a settled job would be polled forever.
          setState((previous) =>
            previous.job !== null && sameJob(previous.job, next)
              ? previous
              : { ...previous, status: "ready", job: next, message: null, statusCode: null },
          );
          if (next.state === "queued" || next.state === "leased")
            timer = window.setTimeout(() => void step(), pollMilliseconds);
        } catch (error: unknown) {
          if (disposed || isAbort(error)) return;
          // A task the server no longer knows about cannot be resumed, so the reader is told to analyze
          // again rather than left with a job that will never move.
          const lost = failure(error);
          if (isAccessFailure(lost.statusCode)) {
            setState((previous) => ({ ...previous, job: null, status: "error", ...lost }));
            return;
          }
          timer = window.setTimeout(() => void step(), pollMilliseconds * 3);
        }
        return;
      }
    };

    if (active) timer = window.setTimeout(() => void step(), pollMilliseconds);
    else void step();
    return () => {
      disposed = true;
      window.clearTimeout(timer);
    };
  }, [client, libraryId, section, state.job, revision, load]);

  // Changing section, closing the open group, or a report becoming readable changes what this page is
  // reading. The switch is the report's identity — its task and the version the server holds for it —
  // because a poll that changes nothing else must not restart the read, while a recheck that files a new
  // version of the same task must. A read that is wanted while another is on the wire is handed to the
  // running one by `load` and served the moment it ends, so this effect never waits for a busy wire.
  const reportSwitch =
    state.job === null || !state.job.report_available ? "" : `${state.job.task_id}:${reportVersion ?? ""}`;
  const wantedSlice =
    reportSwitch === ""
      ? null
      : sliceKey(libraryId ?? "", state.job?.task_id ?? "", reportVersion ?? "", section, groupKey);
  // The reader's current ask, recorded as this render states it so `load` can tell "the reader is still
  // here" from "the reader came back": only the first means the answer on screen is the answer to the ask.
  liveWanted.current = state.job === null ? null : sliceIdentity(libraryId ?? "", state.job.task_id, section, groupKey);
  useEffect(() => {
    const job = live.current.job;
    if (libraryId === null || job === null || !job.report_available || wantedSlice === null) return;
    // The answer on screen is this slice at the version the server holds for it, so there is nothing to
    // read. The slice is compared by identity rather than by the versioned key, because the key a render
    // builds is the version the render holds: a version the server has moved on from is not that answer,
    // which is what makes a recheck re-read the report it filed instead of leaving the previous one on
    // screen. An answer the page has already left is not that answer either, so coming back to a section
    // reads it again rather than showing the section the reader left — `load` drops an answer for a slice
    // that is no longer wanted, and this is the read that replaces it.
    const onScreen =
      readVersion.current === reportVersion &&
      readSlice.current === sliceIdentity(libraryId, job.task_id, section, groupKey);
    if (onScreen) return;
    void load(job.task_id, section, groupKey, null, false);
  }, [libraryId, wantedSlice, reportVersion, revision, load, section, groupKey]);

  // A recheck is a durable background task, so filing one only produces a receipt, and this effect follows
  // that receipt's own task until the server has an outcome. A completed recheck has filed the version it
  // produced as the task's new report, so recording that version is what makes the slice on screen read as
  // the superseded one it now is and makes the read switch read the new one. A refused recheck filed no
  // version, so nothing about the version on screen changes.
  useEffect(() => {
    const receipt = state.recheck;
    const job = state.job;
    if (libraryId === null || job === null || receipt === null) return;
    if (receipt.state === "completed" && receipt.analysis_version !== "")
      setReportVersion((previous) =>
        newerThan(previous, receipt.analysis_version) ? receipt.analysis_version : previous,
      );
    if (receipt.state !== "pending" || receipt.recheck_task_id === "") return;
    let disposed = false;
    let timer: number | undefined;
    const step = async () => {
      if (disposed) return;
      // A poll is not sent for a run the page already has an outcome for: the receipt the page holds is
      // that answer, so asking again would only repeat what is already on screen.
      const answered = live.current.recheck;
      if (answered !== null && answered.state !== "pending") return;
      try {
        const next = await client.revalidate(
          libraryId,
          job.task_id,
          receipt.plan_digest,
          operationKey.current,
          new AbortController().signal,
          receipt.recheck_task_id,
        );
        if (disposed || current.current !== libraryId) return;
        setState((previous) => ({ ...previous, status: "ready", recheck: next, message: null, statusCode: null }));
        if (next.state === "pending") timer = window.setTimeout(() => void step(), recheckPollMilliseconds);
      } catch (error: unknown) {
        if (disposed || isAbort(error)) return;
        // A poll that fails does not invent an answer: the receipt stays pending and the page keeps the
        // sentence it already has, so an unread outcome is never shown as a finished comparison.
        timer = window.setTimeout(() => void step(), recheckPollMilliseconds * 3);
      }
    };

    timer = window.setTimeout(() => void step(), recheckPollMilliseconds);
    return () => {
      disposed = true;
      window.clearTimeout(timer);
    };
  }, [client, libraryId, state.job, state.recheck, revision]);

  const execute = useCallback(
    async (action: DedupAction) => {
      if (libraryId === null || mutation.current !== null) return;
      if ((action === "cancel" || action === "recheck" || action === "export") && live.current.job === null) return;
      const controller = new AbortController();
      mutation.current = controller;
      setState((previous) => ({ ...previous, pending: true, message: null, statusCode: null }));
      try {
        await performAction(action, {
          client,
          libraryId,
          state: live.current,
          operationKey: operationKey.current,
          signal: controller.signal,
          adopt: (key) => {
            operationKey.current = key;
          },
          settle: (changes) => {
            // A start or a cancel replaces what the page is showing, so the slice it had is no longer
            // loaded and must be read again when the new job has a report.
            if (changes.page === null) readSlice.current = null;
            if (current.current === libraryId) setState((previous) => ({ ...previous, ...changes }));
          },
          exported: (receipt) => {
            if (current.current === libraryId) exportedReceipt.current?.(receipt);
          },
        });
      } catch (error: unknown) {
        if (isAbort(error) || current.current !== libraryId) return;
        const refused = failure(error);
        // A refused start or cancel is re-read from the server instead of being guessed here.
        if (refused.statusCode === 400 || refused.statusCode === 409) setRevision((value) => value + 1);
        setState((previous) => ({ ...previous, status: "error", ...refused }));
      } finally {
        if (mutation.current === controller) mutation.current = null;
        if (current.current === libraryId) setState((previous) => ({ ...previous, pending: false }));
      }
    },
    [client, libraryId],
  );

  const visible: DedupView =
    current.current === libraryId ? state : { ...emptyDedupView, status: libraryId === null ? "idle" : "loading" };
  // A recheck files a new version, and between that filing and this page's read of it the version on
  // screen is superseded. An export names the version it writes, so it is not offered in that window: the
  // comparison is between the version the server answered with and the version the server last named.
  const stale = visible.page !== null && readVersion.current !== reportVersion;
  return {
    ...visible,
    kind: section,
    reportVersion,
    stale,
    openGroup: (group: DedupGroup) => setGroupKey(group.group_key),
    closeGroup: () => setGroupKey(null),
    more: () => {
      const job = live.current.job;
      const cursor = live.current.page?.next_cursor ?? null;
      if (job === null || cursor === null) return;
      void load(job.task_id, section, groupKey, cursor, true);
    },
    start: () => void execute("start"),
    cancel: () => void execute("cancel"),
    runRecheck: () => void execute("recheck"),
    exportPlan: () => void execute("export"),
  };
}
