import type { DedupFindingKind, DedupGroup, DedupJob, DedupPage } from "../dedup/dedupTypes";

/**
 * Names one read: the library, the task at the version the answer belongs to, the section and the open
 * group. It is the same string the page uses to decide whether what is on screen is what it asked for, so
 * an answer can be compared against the page's current want without going through state.
 */
export function sliceKey(
  libraryId: string,
  taskId: string,
  version: string,
  kind: DedupFindingKind,
  groupKey: string | null,
): string {
  return [libraryId, `${taskId}:${version}`, kind, groupKey ?? ""].join("\u0000");
}

/**
 * The same read without its version: which section, of which task, of which library, with which group
 * open. Two reads that share this identity are the same slice of the report, so a read that brought the
 * version the page did not know yet is still the read the page wanted, and only a different section or a
 * different group means the reader has moved on.
 */
export function sliceIdentity(
  libraryId: string,
  taskId: string,
  kind: DedupFindingKind,
  groupKey: string | null,
): string {
  return [libraryId, taskId, kind, groupKey ?? ""].join("\u0000");
}

/**
 * The task a version belongs to, and the generation within it. A version is the server's own name for a
 * report: the task's identity, then the generation that task has reached, so a comparison is only
 * meaningful inside one task and a number taken across two of them means nothing.
 */
function parts(version: string): { task: string; generation: number } {
  const colon = version.lastIndexOf(":");
  const generation = Number.parseInt(version.slice(colon + 1), 10);
  return { task: colon < 0 ? "" : version.slice(0, colon), generation };
}

/**
 * Whether a version the server named is at least as new as the one the page is holding, which is what
 * decides whether an answer may be written: a report only moves forward within a task, so an answer for a
 * later generation is the newer report and is taken, and an answer for an earlier one is the superseded
 * report and is refused. Two names for different tasks are not comparable at all — a generation is only
 * ordered inside the task that owns it — so they are compared as their task ids are: a name that differs
 * from the held one in its task is taken as later, which keeps a name the page does not understand from
 * freezing it on an older report.
 */
export function newerThan(current: string | null, next: string): boolean {
  if (current === null || current === next) return true;
  const held = parts(current);
  const answered = parts(next);
  if (held.task !== answered.task) return true;
  if (Number.isNaN(held.generation) || Number.isNaN(answered.generation)) return true;
  return answered.generation >= held.generation;
}

/**
 * Whether a status answer says anything the page is not already showing. A durable task's own update time
 * only moves when its state, its cancellation record or its lease changes, so comparing it together with
 * the fields the page renders is enough to tell a real update from the same facts parsed again.
 */
export function sameJob(current: DedupJob, next: DedupJob): boolean {
  return (
    current.task_id === next.task_id &&
    current.state === next.state &&
    current.report_available === next.report_available &&
    current.cancellation_requested === next.cancellation_requested &&
    current.can_cancel === next.can_cancel &&
    current.can_retry === next.can_retry &&
    current.analysis_version === next.analysis_version &&
    current.updated_at === next.updated_at &&
    current.failure_code === next.failure_code
  );
}

/**
 * The open group as the detail renders it, built from the members the server answered with. A group page
 * is the only answer that carries members, so a section answer yields an empty member list rather than a
 * group invented from the section listing.
 */
export function groupFrom(groupKey: string, page: DedupPage): DedupGroup {
  return {
    group_key: groupKey,
    length: page.items[0]?.length ?? 0,
    evidence_hash: page.items[0]?.sha256 ?? "",
    member_count: page.total,
    identity_merge_proposed: false,
    members: page.items,
  };
}
