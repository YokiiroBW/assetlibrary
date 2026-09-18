import type { DedupFindingKind, DedupGroup, DedupJob, DedupPage, DedupRecheck } from "./dedupTypes";

/**
 * What one dedup workbench session has on screen. It is the state the hook holds and the state the page
 * reads, and it is stated apart from the hook so the page can be typed on the value it receives without
 * importing the module that produces it.
 */
export interface DedupView {
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

/** A session that has not started: no job, no report, nothing asked of the server yet. */
export const emptyDedupView: DedupView = {
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

/** The facts a workbench session reports, all of them the server's own answers. */
export interface DedupJobState extends Omit<DedupView, "status"> {
  readonly status: DedupView["status"];
  readonly kind: DedupFindingKind;
  /** The report version the server currently holds, as the server itself named it. */
  readonly reportVersion: string | null;
  /** Whether the version on screen is still the server's current one. An export requires that it is. */
  readonly stale: boolean;
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
