import type { MutableRefObject } from "react";
import type { DedupClient } from "../dedup/dedupClient";
import { groupFrom, newerThan, sliceIdentity, sliceKey } from "./dedupSlices";
import { isAbort, failure } from "./queryState";
import type { DedupFindingKind, DedupPage } from "../dedup/dedupTypes";
import type { DedupView } from "../dedup/dedupJobState";

const pageSize = 50;

/**
 * How many times one read may hand the wire to the read the reader wants before the controller stops
 * recovering on its own. A read that ended without the answer on screen is normally a read that was for
 * the wrong slice, and one more read settles it. The larger case is an answer the page refused because
 * the server had already filed a newer report: recovering once reads that report, which is the point, and
 * the budget is what stops the second refusal — an answer the page refuses is not progress, so recovering
 * again would only repeat it at wire speed. Past the budget the page keeps the state it has: the error a
 * failed read states, or the report it already holds, and the reader's own next action is what starts a
 * read, which is the explicit retry the card asks for instead of a hidden loop.
 */
const maxRecoveries = 2;

/** Everything the read controller needs from the page it serves, all of it live rather than captured. */
export interface LoadContext {
  readonly client: DedupClient;
  readonly libraryId: string | null;
  readonly state: MutableRefObject<DedupView>;
  readonly activeLoad: MutableRefObject<string | null>;
  readonly reading: MutableRefObject<string | null>;
  /** The library the page is open on now. A read whose library is gone is not written to the page. */
  readonly current: MutableRefObject<string | null>;
  readonly liveVersion: MutableRefObject<string | null>;
  readonly readSlice: MutableRefObject<string | null>;
  readonly readVersion: MutableRefObject<string | null>;
  readonly liveSection: MutableRefObject<DedupFindingKind>;
  readonly liveGroup: MutableRefObject<string | null>;
  readonly liveWanted: MutableRefObject<string | null>;
  readonly query: MutableRefObject<AbortController | null>;
  /** The read that owns the wire, by request key, so a request is never blamed for another's cancellation. */
  readonly taken: MutableRefObject<string | null>;
  /** The slice whose read failed, so a failure is stated rather than retried the moment it is stated. */
  readonly failed: MutableRefObject<string | null>;
  /**
   * Consecutive recoveries the controller has made, so the recovery itself stays bounded. Only progress
   * resets it — an answer written for the slice the reader wants, at the version the page holds — because
   * a refused answer, a failure and a cancelled read are all reads that ended without serving the reader.
   */
  readonly recovery: MutableRefObject<number>;
  /**
   * The slice and version of the answer the page last put on screen, and the reader's goal that was being
   * served when it did. A read dispatched for a goal the served answer does not already cover is the
   * reader asking for something new, and that is the other thing that makes the budget whole again.
   */
  readonly served: MutableRefObject<string | null>;
  readonly readerGoal: MutableRefObject<string | null>;
  /** The slice the reader wants and the wire has not served yet, if a read is on its way to serve it. */
  readonly pending: MutableRefObject<string | null>;
  readonly setState: (update: (previous: DedupView) => DedupView) => void;
  readonly setReportVersion: (update: (previous: string | null) => string | null) => void;
}

/** One read: the task, the section or open group, the paging cursor, and whether it pages the list. */
export type LoadSlice = (
  taskId: string,
  kind: DedupFindingKind,
  groupKey: string | null,
  cursor: string | null,
  more: boolean,
) => Promise<void>;

/**
 * Builds the read controller of one dedup workbench session.
 *
 * Request control rests on three facts, all of them refs so that a re-render cannot reset them:
 * - what the reader wants (`liveSection`, `liveGroup`, `liveVersion`), recorded as each render states it;
 * - what is on screen (`readSlice`, `readVersion`), recorded only when an answer is applied;
 * - what is being read (`reading`), recorded when a request goes out and cleared when it ends.
 *
 * From those, at most one read is in flight for the slice the reader wants, and it is dispatched only if
 * that slice's answer is not already on screen. A read wanted while another is on the wire preempts it:
 * the read for the slice the reader left is aborted and its place taken, so what is being read is always
 * the slice they selected rather than the one they passed through. An answer is written only if it is for
 * the slice the reader wants and for a version that is at least as new as the one the page holds; an
 * answer for a section, a group or a version they have moved on from is dropped, and the read that ends
 * recovers the slice the reader wants — within a bound, so a refusal can never become a loop.
 */
export function useSliceLoader(context: LoadContext): LoadSlice {
  const {
    client,
    libraryId,
    state,
    activeLoad,
    reading,
    liveVersion,
    readSlice,
    readVersion,
    liveSection,
    liveGroup,
    liveWanted,
    query,
    current,
    taken,
    failed,
    recovery,
    served,
    readerGoal,
    pending,
    setState,
    setReportVersion,
  } = context;
  const load: LoadSlice = async (
    taskId: string,
    kind: DedupFindingKind,
    groupKey: string | null,
    cursor: string | null,
    more: boolean,
  ) => {
    // Whether the read reached the server at all. A read that failed is stated as an error and is not
    // recovered from: reading the same thing again would fail the same way, so the page keeps the error it
    // states and the reader's own next action is the retry.
    let failedRead = false;
    {
      if (libraryId === null) return;
      const requestKey = [libraryId, taskId, kind, groupKey, cursor, more].join("\u0000");
      // What this read asks for. It is compared at answer time against the reader's own current section and
      // group, never against this call's arguments, because by then those describe what they asked for
      // before — which is the whole point of the comparison.
      const asked = sliceIdentity(libraryId, taskId, kind, groupKey);
      // The answer this read would fetch is already on screen and the reader is on that slice, so there is
      // nothing to read. A slice whose answer the page has left, or a version the server has moved on from,
      // is not that answer, so it is read — and reading it takes the place of whatever is on the wire. The
      // version compared against is the page's own: the version the render states, or, in the window between
      // an answer recording its version and the render that states it, the version that answer recorded.
      // Without the second, the read that just answered would not recognise its own answer as being on
      // screen and would read the same slice a second time.
      const version = liveVersion.current ?? readVersion.current ?? "";
      const showing = readSlice.current === sliceKey(libraryId, taskId, version, kind, groupKey);
      // The answer on screen is the answer to this ask only while the reader is still here and no other
      // slice has taken the wire: a read that went out for another slice will answer that slice rather than
      // this one, so the answer on screen is not what this ask is waiting for and it is read again.
      const superseding = activeLoad.current !== null && reading.current !== asked;
      // A read that failed is not read again on the spot: the page states the failure and waits for the
      // reader, so a failure cannot become a read per render. The reader's next action is what reads again —
      // a section, a group or a version that is not the one whose read failed — and dispatching that read is
      // what clears this record.
      if (!more && failed.current === asked) return;
      if (!more && showing && !superseding && liveWanted.current === asked) return;
      // The slice the reader wants is already on the wire, so this is the same read asked for twice and
      // nothing is sent: the answer on its way is the answer to this ask. A read for another slice is one
      // the reader has moved on from, so it is aborted and its place taken rather than left to answer a
      // slice nobody is looking at — what is being read is always the slice they selected.
      if (activeLoad.current !== null) {
        if (reading.current === asked) return;
        activeLoad.current = null;
        query.current?.abort();
        query.current = null;
      }
      activeLoad.current = requestKey;
      reading.current = asked;
      if (!more) failed.current = null;
      // A goal the controller has not been serving yet is the reader asking for something new — they
      // selected another section, another group, or a version the page does not hold — and a new goal gets
      // the whole budget. A read for a goal already being served is not a new goal, whether the effect
      // re-ran for it or the read that ended is recovering it, so it spends the budget instead of resetting
      // it. That is what makes consecutive refusals actually exhaust the budget.
      if (!more && readerGoal.current !== asked) {
        recovery.current = 0;
        readerGoal.current = asked;
      }
      // The read that holds the wire, recorded per request: the read it displaced can then tell that it was
      // displaced — and that the read which took its place is the one that will serve the reader — instead
      // of reading a flag another request may have written.
      taken.current = requestKey;
      // The wire is no longer free for the slice the reader wanted, so that want is no longer pending: this
      // read answers the slice it was asked for, and the read that ends is what recovers anything else.
      if (!more) pending.current = null;
      const controller = new AbortController();
      query.current = controller;
      // A read that replaces what is already on screen keeps the list in place: removing it would move the
      // rows the reader is looking at and take the focus target away with them. The first read of a slice
      // the page holds no answer for is the one that may state it is loading; a read of a slice the page has
      // left does not blank what is on screen, it replaces it when its own answer arrives.
      if (more) setState((previous) => ({ ...previous, loadingMore: true }));
      else if (state.current.page === null && !showing) setState((previous) => ({ ...previous, status: "loading" }));
      try {
        const page: DedupPage = await client.results(
          libraryId,
          taskId,
          { kind, groupKey: groupKey ?? undefined, cursor, pageSize },
          controller.signal,
        );
        if (current.current !== libraryId || controller.signal.aborted) return;
        // An answer for a slice the reader has left is not written to the page, and it does not become the
        // record of what is on screen either: the reader is looking at another section or another group, so
        // this answer is not the page's answer. What is compared is the slice, not the version, because a
        // read that was wanted before the page knew the report version is still the read the page wanted —
        // the version it belongs to is the one the server names in this very answer.
        const nowWanted = sliceIdentity(libraryId, taskId, liveSection.current, liveGroup.current);
        if (!more && asked !== nowWanted) return;
        // The version this answer belongs to is the one the server names in it, and the server's own name is
        // the authority for it rather than an increment derived here. An answer that names no version is the
        // version the page asked for, read live rather than from this call's closure: what the page asked for
        // when the read went out is not what it is asking for now. An answer that names a version older than
        // the one the page holds is about a report the server has already replaced, so it is refused and the
        // page keeps the newer report it has; an answer that names a newer one — a recheck another reader or
        // this page's own recheck has filed — is the report the server holds now and is taken.
        const answered = page.analysis_version === "" ? (liveVersion.current ?? "") : page.analysis_version;
        if (liveVersion.current !== null && !newerThan(liveVersion.current, answered)) {
          return;
        }
        // The record is the switch the read effect compares against, so it is built the same way: the task
        // and the version the answer belongs to, then the section and the open group.
        readSlice.current = sliceKey(libraryId, taskId, answered, kind, groupKey);
        readVersion.current = answered;
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
        if (!more && answered !== "")
          setReportVersion((previous) => (newerThan(previous, answered) ? answered : previous));
        // The reader's answer is on screen: this read served the goal it was made for, so the budget is whole
        // again. This is the only progress that resets it — a refusal, a failure and a cancellation all end
        // without reaching this line, and none of them may stand in for progress.
        if (!more && asked === nowWanted && readerGoal.current === asked) {
          served.current = `${asked}\u0000${answered}`;
          recovery.current = 0;
        }
      } catch (error: unknown) {
        if (isAbort(error)) return;
        failedRead = true;
        if (!more) failed.current = asked;
        setState((previous) => ({ ...previous, loadingMore: false, status: "error", ...failure(error) }));
      } finally {
        if (activeLoad.current === requestKey) activeLoad.current = null;
        if (reading.current === asked) reading.current = null;
        if (query.current === controller) query.current = null;
        // The read that just ended is the only thing that knows the wire is free, so it is the one that
        // starts the next read — waiting for an effect to notice would lose the wake-up, because an effect
        // cannot run while the wire was busy. What it reads is what the reader wants now, not what this read
        // was for: they may have moved to another section, another group, or a version the server filed while
        // it was on the wire, and what is on screen is the answer to the slice they left rather than the one
        // they selected.
        //
        // Only the read that still owns the wire recovers: a read the reader's next selection displaced was
        // cancelled by that selection, and the read that took its place is the one on its way to answer the
        // reader, so this one has nothing to start.
        if (taken.current !== requestKey) return;
        taken.current = null;
        if (failedRead) return;
        // A read that ended without putting the reader's answer on screen may recover, and the budget is what
        // bounds it: recovering once is what reads the version the server filed when the answer in hand was
        // refused as superseded, and the budget is what stops the refusal that follows — an answer the page
        // refuses is not progress, so recovering again would repeat it at wire speed. The budget is spent by
        // every one of these endings and is only made whole by progress or by the reader asking for something
        // new, so consecutive refusals exhaust it. Past it the page keeps the state the refusal left: the error
        // a failed read states, or the report it already holds, and the reader's next action reads again.
        if (recovery.current >= maxRecoveries) {
          return;
        }
        const job = state.current.job;
        if (job === null || !job.report_available) return;
        const nowWanted = sliceIdentity(libraryId, job.task_id, liveSection.current, liveGroup.current);
        // What is on screen is the reader's own slice when the answer on screen is the one this read just
        // wrote for it, at the version the page holds — which this render may not have caught up with yet. A
        // slice the reader has left, or one whose answer the server has since superseded, is not that answer,
        // so it is read rather than left standing.
        const onScreen =
          readSlice.current ===
            sliceKey(libraryId, job.task_id, readVersion.current ?? "", liveSection.current, liveGroup.current) &&
          readVersion.current === (liveVersion.current ?? "");
        if (onScreen) return;
        // A read for that slice is already on the wire, so it will answer it and this one is not repeated.
        if (reading.current === nowWanted) return;
        recovery.current += 1;
        pending.current = nowWanted;
        void load(job.task_id, liveSection.current, liveGroup.current, null, false);
      }
    }
  };
  // The controller never captures a render: everything it reads about the page it reads through the refs
  // it was given, so a new one per render behaves exactly like the previous one.
  return load;
}
