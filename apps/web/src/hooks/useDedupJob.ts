import { useCallback, useEffect, useRef, useState } from "react";
import { failure, isAbort, isAccessFailure } from "./queryState";
import { DedupClient } from "../dedup/dedupClient";
import type {
  DedupExportReceipt,
  DedupFindingKind,
  DedupGroup,
  DedupJob,
  DedupPage,
  DedupRecheck,
} from "../dedup/dedupTypes";

const pollMilliseconds = 2_000;
const pageSize = 50;
const recheckPollMilliseconds = 1_500;

/** One read of one slice of a report version. */
interface LoadParameters {
  taskId: string;
  kind: DedupFindingKind;
  groupKey: string | null;
  cursor: string | null;
  more: boolean;
}

type LoadSection = (
  taskId: string,
  kind: DedupFindingKind,
  groupKey: string | null,
  cursor: string | null,
  more: boolean,
) => Promise<void>;

interface DedupView {
  status: "idle" | "loading" | "ready" | "error";
  pending: boolean;
  loadingMore: boolean;
  job: DedupJob | null;
  page: DedupPage | null;
  group: DedupGroup | null;
  recheck: DedupRecheck | null;
  message: string | null;
  statusCode: number | null;
}

const initial: DedupView = {
  status: "idle",
  pending: false,
  loadingMore: false,
  job: null,
  page: null,
  group: null,
  recheck: null,
  message: null,
  statusCode: null,
};

export interface DedupJobState {
  readonly status: DedupView["status"];
  readonly pending: boolean;
  readonly loadingMore: boolean;
  readonly job: DedupJob | null;
  readonly page: DedupPage | null;
  readonly group: DedupGroup | null;
  readonly recheck: DedupRecheck | null;
  readonly message: string | null;
  readonly statusCode: number | null;
  readonly kind: DedupFindingKind;
}

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
  const [state, setState] = useState<DedupView>(initial);
  const [revision, setRevision] = useState(0);
  /** The group whose members this page is showing. Null means the section listing is shown. */
  const [groupKey, setGroupKey] = useState<string | null>(null);
  const query = useRef<AbortController | null>(null);
  const mutation = useRef<AbortController | null>(null);
  const current = useRef(libraryId);
  const live = useRef(state);
  live.current = state;
  const operationKey = useRef<string>(crypto.randomUUID());
  const activeLoad = useRef<string | null>(null);
  /** The slice whose answer is on screen. A cleared page forgets it, so the slice can be read again. */
  const fulfilled = useRef<string | null>(null);
  // A read asked for while another one is on the wire, and a counter that wakes the effect serving it.
  const queued = useRef<LoadParameters | null>(null);
  const [queuedRevision, setQueuedRevision] = useState(0);
  const exportedReceipt = useRef(onExported);
  exportedReceipt.current = onExported;
  useEffect(() => {
    current.current = libraryId;
    operationKey.current = crypto.randomUUID();
    activeLoad.current = null;
    fulfilled.current = null;
    queued.current = null;
    setGroupKey(null);
    mutation.current?.abort();
    query.current?.abort();

    // Switching library, or losing it, must not leave the previous library's findings on screen. No
    // analysis is requested here: the reader's own start is the only thing that begins one.
    setState(libraryId === null ? initial : { ...initial, status: "loading" });
    setRevision((value) => value + 1);
  }, [client, libraryId]);

  /**
   * Loads one section of the open report version. A missing report is not an empty result: the page
   * states that it must be analyzed again instead of showing zero duplicates.
   */
  // A read asked for while another one is on the wire is served by this handle once the wire is free,
  // so the handle is typed on its own rather than inferred through the callback it points at.
  const load: LoadSection = useCallback(
    async (taskId: string, kind: DedupFindingKind, groupKey: string | null, cursor: string | null, more: boolean) => {
      if (libraryId === null) return;
      // A read already on the wire is not interrupted. A different read that is wanted now is queued
      // instead, so a rapid open-and-close cannot leave the page without the list it still needs.
      const requestKey = [libraryId, taskId, kind, groupKey, cursor, more].join("\u0000");
      if (activeLoad.current !== null) {
        queued.current = { taskId, kind, groupKey, cursor, more };
        setQueuedRevision((value) => value + 1);
        return;
      }
      activeLoad.current = requestKey;
      // The slice is recorded as loaded when the answer is applied rather than when the read starts: the
      // page must be able to ask again for a slice whose answer was dropped or whose page was cleared.
      const sliceKey = [libraryId, taskId, kind, groupKey].join("\u0000");
      const controller = new AbortController();
      query.current?.abort();
      query.current = controller;
      // A read that replaces what is already on screen keeps the list in place: removing it would move
      // the rows the reader is looking at and take the focus target away with them.
      if (more) setState((previous) => ({ ...previous, loadingMore: true }));
      else if (live.current.page === null) setState((previous) => ({ ...previous, status: "loading" }));
      try {
        const page = await client.results(
          libraryId,
          taskId,
          { kind, groupKey: groupKey ?? undefined, cursor, pageSize },
          controller.signal,
        );
        if (current.current !== libraryId || controller.signal.aborted) return;
        if (!more) fulfilled.current = sliceKey;
        setState((previous) => ({
          ...previous,
          status: "ready",
          loadingMore: false,
          page,
          group: groupKey === null ? null : groupFrom(groupKey, page),
          recheck: groupKey === null ? previous.recheck : null,
          message: null,
          statusCode: null,
        }));
      } catch (error: unknown) {
        if (isAbort(error)) return;
        setState((previous) => ({ ...previous, loadingMore: false, status: "error", ...failure(error) }));
      } finally {
        if (activeLoad.current === requestKey) activeLoad.current = null;
        if (query.current === controller) query.current = null;
      }
    },
    [client, libraryId],
  );

  // The read that was asked for while another one was on the wire, served as soon as the wire is free.
  useEffect(() => {
    const wanted = queued.current;
    if (wanted === null || activeLoad.current !== null) return;
    queued.current = null;
    void load(wanted.taskId, wanted.kind, wanted.groupKey, wanted.cursor, wanted.more);
  }, [queuedRevision, load]);

  // Reading a report and following its progress are the same effect: a succeeded job with a retained
  // report is loaded exactly once, and an active job is re-read until it settles.
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
          setState((previous) => ({ ...previous, status: "ready", job: next, message: null, statusCode: null }));
          if (next.state === "queued" || next.state === "leased")
            timer = window.setTimeout(() => void step(), pollMilliseconds);
        } catch (error: unknown) {
          if (disposed || isAbort(error)) return;
          // A task the server no longer knows about cannot be resumed; the reader is told to analyze
          // again rather than being left with a job that will never move.
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

  // Changing section, closing the open group, or a report becoming readable is a change of what this
  // page is reading, so the slice is read. The switch is the report's identity rather than the job
  // object, because a poll that changes nothing else must not restart the read.
  const reportSwitch = state.job === null ? "" : `${state.job.task_id}:${state.job.report_available}`;
  const wantedSlice =
    reportSwitch === "" ? null : `${libraryId ?? ""}\u0000${reportSwitch}\u0000${section}\u0000${groupKey ?? ""}`;
  useEffect(() => {
    const job = live.current.job;
    if (libraryId === null || job === null || !job.report_available || wantedSlice === null) return;
    if (fulfilled.current === wantedSlice) return;
    void load(job.task_id, section, groupKey, null, false);
  }, [libraryId, wantedSlice, revision, load]);

  // A recheck is a durable background task, so filing one only produces a receipt. This effect follows
  // that receipt's own task until the server has an outcome, which is why the recheck button finishes
  // immediately: the comparison runs on the background path rather than inside the button's request.
  useEffect(() => {
    const receipt = state.recheck;
    const job = state.job;
    if (libraryId === null || job === null || receipt === null) return;
    if (receipt.state !== "pending" || receipt.recheck_task_id === "") return;
    let disposed = false;
    let timer: number | undefined;
    const step = async () => {
      if (disposed) return;
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
    async (action: "start" | "cancel" | "recheck" | "export") => {
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
            if (changes.page === null) fulfilled.current = null;
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
    current.current === libraryId ? state : { ...initial, status: libraryId === null ? "idle" : "loading" };
  return {
    ...visible,
    kind: section,
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

/** The value the page receives: the server's facts plus the operations it may ask for. */
export interface DedupJobHandle extends DedupJobState {
  openGroup: (group: DedupGroup) => void;
  closeGroup: () => void;
  more: () => void;
  start: () => void;
  cancel: () => void;
  runRecheck: () => void;
  exportPlan: () => void;
}

interface ActionContext {
  client: DedupClient;
  libraryId: string;
  state: DedupView;
  operationKey: string;
  signal: AbortSignal;
  settle: (changes: Partial<DedupView>) => void;
  /** Records the operation key the task now on screen belongs to, so later operations name it. */
  adopt: (operationKey: string) => void;
  exported: (receipt: DedupExportReceipt) => void;
}

/**
 * Performs one operation and records only what the server answered. A successful start or cancel never
 * fabricates a result set: the page shows the job's own state and waits for a report. An analysis only
 * ever begins because the reader asked for one; opening the page never starts a run.
 */
async function performAction(action: "start" | "cancel" | "recheck" | "export", context: ActionContext) {
  if (action === "start") {
    // The page restates the budget the server accepted for the version on screen, so a new analysis of
    // the same library runs under the ceiling the reader has already been shown. Before any version
    // exists the page states no budget at all and the server applies its own.
    const limits = context.state.job?.limits;
    // The first request of this mount names the operation by the library, so pressing the button twice
    // without a reload resolves to the same durable task instead of reading the same directories twice.
    const known = await context.client.start(context.libraryId, context.libraryId, false, context.signal, limits);
    // A version that is still running, or one that finished and whose report the server can still serve,
    // is the version this library has. Only a run that already ended without a readable report leaves the
    // reader's request to be carried out as a new analysis under a key of its own.
    const job = retained(known)
      ? known
      : await context.client.start(context.libraryId, context.operationKey, false, context.signal, limits);
    context.adopt(job.task_id === known.task_id ? context.libraryId : context.operationKey);
    context.settle({
      status: "ready",
      job,
      page: null,
      group: null,
      recheck: null,
      message: null,
      statusCode: null,
    });
    return;
  }

  if (action === "cancel") {
    const job = await context.client.cancel(context.libraryId, context.operationKey, context.signal);
    context.settle({ status: "ready", job, message: null, statusCode: null });
    return;
  }
  const job = context.state.job;
  if (job === null) return;
  if (action === "recheck") {
    // Filing a recheck answers with a receipt; the page's own effect follows that task to its outcome.
    // The receipt replaces any earlier recheck, so a new request never displays the previous answer.
    const recheck = await context.client.revalidate(
      context.libraryId,
      job.task_id,
      context.state.page?.summary.plan_digest ?? "",
      context.operationKey,
      context.signal,
    );
    context.settle({ status: "ready", recheck, message: null, statusCode: null });
    return;
  }

  const document = await context.client.exportPlan(
    context.libraryId,
    job.task_id,
    context.state.page?.summary.analysis_version ?? job.analysis_version,
    context.state.page?.summary.plan_digest ?? "",
    context.signal,
  );
  const fileName = `dedup-plan-${job.task_id}.json`;
  save(document, fileName);
  context.exported({
    analysis_version: document.analysis_version,
    file_name: fileName,
    plan_digest: document.plan_digest,
  });
  context.settle({ status: "ready", message: null, statusCode: null });
}

/**
 * Whether the library's own version is worth showing as it stands. A queued or leased analysis is still
 * running, a finished one with a retained report can be read, and a finished one whose report was
 * discarded is still the version the library has: it is reported as unreadable rather than started over.
 */
function retained(job: DedupJob): boolean {
  return job.state === "queued" || job.state === "leased" || job.state === "succeeded";
}

function groupFrom(groupKey: string, page: DedupPage): DedupGroup {
  return {
    group_key: groupKey,
    length: page.items[0]?.length ?? 0,
    evidence_hash: page.items[0]?.sha256 ?? "",
    member_count: page.total,
    identity_merge_proposed: false,
    members: page.items,
  };
}

/**
 * Saves the exported plan under a name that names the job it came from. It writes only the plan the
 * server produced: nothing here can move, copy or delete a source file.
 */
function save(document: unknown, fileName: string) {
  const blob = new Blob([JSON.stringify(document, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = window.document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  anchor.rel = "noopener";
  anchor.click();
  URL.revokeObjectURL(url);
}
