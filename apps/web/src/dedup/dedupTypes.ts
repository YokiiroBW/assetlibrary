import type { LibraryCategory } from "../types";

/**
 * One registered library offered as a dedup source. The browser only ever sends the identifier: the
 * physical root is resolved by the server from its own library query.
 */
export interface DedupLibraryChoice {
  library_id: string;
  display_name: string;
  category: LibraryCategory;
  availability: "online" | "offline";
}

export type DedupJobState = "queued" | "leased" | "succeeded" | "failed" | "cancelled";

export interface DedupJob {
  task_id: string;
  library_id: string;
  library_display_name: string;
  state: DedupJobState;
  cancellation_requested: boolean;
  can_cancel: boolean;
  can_retry: boolean;
  report_available: boolean;
  analysis_version: string;
  created_at: string;
  updated_at: string;
  failure_code: string | null;
  retention_notice: string;
  read_only_notice: string;
  /**
   * The budget the server accepted this job under. The page states this rather than a ceiling of its
   * own: a number the page invented would describe a limit nothing enforces.
   */
  limits: DedupLimits;
}

export interface DedupLimits {
  maximum_files: number;
  maximum_bytes: number;
  maximum_file_bytes: number;
  hash_concurrency: number;
}

export interface DedupStatistics {
  observed_entries: number;
  analyzed_files: number;
  not_read_files: number;
  failed_files: number;
  skipped_files: number;
  byte_duplicate_groups: number;
  byte_duplicate_files: number;
  byte_duplicate_bytes: number;
  read_bytes: number;
  additional_read_attempts: number;
}

/**
 * The analysis' own completeness statement. A scan that hit its bounds or could not read a source says
 * so here, so the page can show incompleteness instead of letting it read as "nothing found".
 */
export interface DedupPlanStatement {
  status: string;
  status_text: string;
  unreadable_paths: readonly string[];
  incomplete_reason_count: number;
  scan_bounds_reached: boolean;
  failure_code: string | null;
  source_failures: readonly { source_id: string; reason_code: string }[];
}

export interface DedupSummary {
  policy_version: string;
  analyzed_at: string;
  plan_digest: string;
  statistics: DedupStatistics;
  plan: DedupPlanStatement;
  duplicate_group_count: number;
  duplicate_file_count: number;
  unverified_count: number;
  unreadable_count: number;
  unique_count: number;
  retained_item_count: number;
  truncated: boolean;
  retention_notice: string;
  analysis_version: string;
}

export interface DedupItem {
  source_id: string;
  root: string;
  relative_path: string;
  name: string;
  length: number;
  sha256: string | null;
  structure_hash: string | null;
  last_write_time_utc: string;
  state: string;
  state_text: string;
  read_state: string;
  read_state_text: string;
  failure: string;
  skip_reason: string;
  group_key: string | null;
  category: string;
  relations: readonly string[];
  relation_notes: readonly string[];
}

export interface DedupGroup {
  group_key: string;
  length: number;
  evidence_hash: string;
  member_count: number;
  identity_merge_proposed: boolean;
  members: readonly DedupItem[];
}

export interface DedupPage {
  task_id: string;
  analysis_version: string;
  kind: DedupFindingKind;
  kind_text: string;
  offset: number;
  page_size: number;
  total: number;
  next_cursor: string | null;
  report_available: boolean;
  groups: readonly DedupGroup[];
  items: readonly DedupItem[];
  summary: DedupSummary;
}

export type DedupFindingKind = "ByteDuplicateGroup" | "Unverified" | "Unreadable" | "Unique";

/**
 * The reviewable sections of a report, in the order the page offers them. "unique" is deliberately
 * absent: a file with no same-length peer was never compared, so this page never lists one as unique.
 */
export const dedupKinds: readonly { kind: DedupFindingKind; label: string; note: string }[] = [
  { kind: "ByteDuplicateGroup", label: "重复组", note: "组内每个文件的完整强哈希一致，可逐组复核。" },
  { kind: "Unverified", label: "未验证内容", note: "本次没有读取这些文件的内容，不能据此判断是否重复。" },
  { kind: "Unreadable", label: "本次不可读", note: "本次读取失败或权限被拒绝，文件本身未必有问题。" },
];

/**
 * The answer to a recheck. A recheck is a durable background task, so this shape has two states: a
 * receipt that names the task and the version it will verify, and the outcome once that task has one.
 * The state travels as its own field, because a page must never read "not finished" as "nothing changed".
 */
export type DedupRecheckState = "pending" | "completed" | "refused";

export interface DedupRecheck {
  state: DedupRecheckState;
  state_text: string;
  task_id: string;
  /** The recheck's own durable task. A poll names it so the server answers about this run, not a newer one. */
  recheck_task_id: string;
  completed: boolean;
  status: string;
  status_text: string;
  plan_still_current: boolean;
  reasons: readonly string[];
  changed_paths: readonly string[];
  disappeared_paths: readonly string[];
  new_paths: readonly string[];
  plan_digest: string;
  previous_plan_digest: string | null;
  analysis_version: string;
  failure_code: string | null;
  report_available: boolean;
  retention_notice: string;
  read_only_notice: string;
}

/**
 * What the page reports after an export: the version that was written and the file name it was saved
 * under. The document itself is what the browser saved; the page never re-derives it.
 */
export interface DedupExportReceipt {
  analysis_version: string;
  file_name: string;
  plan_digest: string;
}

export interface DedupExportPlan {
  format_version: number;
  document_type: string;
  task_id: string;
  analysis_version: string;
  library_id: string;
  library_display_name: string;
  policy_version: string;
  analyzed_at: string;
  exported_at: string;
  plan_digest: string;
  retention_notice: string;
  grants_file_operation: boolean;
  read_only_notice: string;
  limits: DedupLimits;
  groups: readonly { group_key: string; length: number; evidence_hash: string; evidence_basis: string }[];
  unverified: readonly DedupItem[];
  unreadable: readonly DedupItem[];
  truncated: boolean;
  recheck: { performed: boolean; status: string; reasons: readonly string[]; performed_at: string | null };
}
