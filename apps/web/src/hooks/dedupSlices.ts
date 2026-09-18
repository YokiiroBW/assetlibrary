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
 * Whether a version the server named is later than the one the page is holding. Versions are the task's
 * identity and its generation, and a generation only ever moves forward for a task, so the number after
 * the last colon is the whole comparison. A version that cannot be read that way is taken as later, which
 * keeps a name the page does not understand from freezing it on an older one.
 */
export function newerThan(current: string | null, next: string): boolean {
  if (current === null || current === next) return true;
  const held = Number.parseInt(current.slice(current.lastIndexOf(":") + 1), 10);
  const answered = Number.parseInt(next.slice(next.lastIndexOf(":") + 1), 10);
  if (Number.isNaN(held) || Number.isNaN(answered)) return true;
  return answered >= held;
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
