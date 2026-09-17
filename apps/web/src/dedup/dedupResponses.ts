import { AssetLinkApiError } from "../assetLinkError";
// The response primitives are shared with the shell's own decoders rather than copied: one accepted
// shape means the workbench and the rest of the workspace cannot disagree about what a valid body is.
import { array, boolean, integer, optionalString, record, string } from "../assetLinkResponses";
import type {
  DedupExportPlan,
  DedupGroup,
  DedupItem,
  DedupJob,
  DedupJobState,
  DedupPage,
  DedupPlanStatement,
  DedupRecheck,
  DedupRecheckState,
  DedupStatistics,
  DedupSummary,
} from "./dedupTypes";

const states: readonly DedupJobState[] = ["queued", "leased", "succeeded", "failed", "cancelled"];
const kinds = ["ByteDuplicateGroup", "Unverified", "Unreadable", "Unique"] as const;
const recheckStates: readonly DedupRecheckState[] = ["pending", "completed", "refused"];

export function decodeJob(value: unknown): DedupJob {
  const body = record(value, "dedup job");
  const state = string(body.state, "state");
  if (!(states as readonly string[]).includes(state)) throw new TypeError("state is invalid");
  return {
    task_id: string(body.task_id, "task_id"),
    library_id: string(body.library_id, "library_id"),
    library_display_name: string(body.library_display_name, "library_display_name"),
    state: state as DedupJobState,
    cancellation_requested: boolean(body.cancellation_requested, "cancellation_requested"),
    can_cancel: boolean(body.can_cancel, "can_cancel"),
    can_retry: boolean(body.can_retry, "can_retry"),
    report_available: boolean(body.report_available, "report_available"),
    analysis_version: string(body.analysis_version, "analysis_version"),
    created_at: string(body.created_at, "created_at"),
    updated_at: string(body.updated_at, "updated_at"),
    failure_code: optionalString(body.failure_code, "failure_code"),
    retention_notice: string(body.retention_notice, "retention_notice"),
    read_only_notice: string(body.read_only_notice, "read_only_notice"),
    limits: decodeLimits(body.limits),
  };
}

export function decodePage(value: unknown): DedupPage {
  const body = record(value, "dedup page");
  const kind = string(body.kind, "kind");
  if (!(kinds as readonly string[]).includes(kind)) throw new TypeError("kind is invalid");
  return {
    task_id: string(body.task_id, "task_id"),
    analysis_version: string(body.analysis_version, "analysis_version"),
    kind: kind as DedupPage["kind"],
    kind_text: string(body.kind_text, "kind_text"),
    offset: integer(body.offset, "offset"),
    page_size: integer(body.page_size, "page_size"),
    total: integer(body.total, "total"),
    next_cursor: optionalString(body.next_cursor, "next_cursor"),
    report_available: boolean(body.report_available, "report_available"),
    groups: array(body.groups, "groups").map(decodeGroup),
    items: array(body.items, "items").map(decodeItem),
    summary: decodeSummary(body.summary),
  };
}

export function decodeRecheck(value: unknown): DedupRecheck {
  const body = record(value, "dedup recheck");
  const state = string(body.state, "state");
  if (!(recheckStates as readonly string[]).includes(state)) throw new TypeError("state is invalid");
  return {
    state: state as DedupRecheckState,
    state_text: string(body.state_text, "state_text"),
    task_id: string(body.task_id, "task_id"),
    recheck_task_id: string(body.recheck_task_id, "recheck_task_id"),
    completed: boolean(body.completed, "completed"),
    // The status is rendered through its server-provided text, so an unknown future status is shown
    // as stated rather than silently mapped onto a local guess.
    status: string(body.status, "status"),
    status_text: string(body.status_text, "status_text"),
    plan_still_current: boolean(body.plan_still_current, "plan_still_current"),
    reasons: array(body.reasons, "reasons").map((reason) => string(reason, "reason")),
    changed_paths: paths(body.changed_paths),
    disappeared_paths: paths(body.disappeared_paths),
    new_paths: paths(body.new_paths),
    plan_digest: string(body.plan_digest, "plan_digest"),
    previous_plan_digest: optionalString(body.previous_plan_digest, "previous_plan_digest"),
    analysis_version: string(body.analysis_version, "analysis_version"),
    failure_code: optionalString(body.failure_code, "failure_code"),
    report_available: boolean(body.report_available, "report_available"),
    retention_notice: string(body.retention_notice, "retention_notice"),
    read_only_notice: string(body.read_only_notice, "read_only_notice"),
  };
}

export function decodeExportPlan(value: unknown): DedupExportPlan {
  const body = record(value, "dedup export");
  const recheck = record(body.recheck, "recheck");
  return {
    format_version: integer(body.format_version, "format_version"),
    document_type: string(body.document_type, "document_type"),
    task_id: string(body.task_id, "task_id"),
    analysis_version: string(body.analysis_version, "analysis_version"),
    library_id: string(body.library_id, "library_id"),
    library_display_name: string(body.library_display_name, "library_display_name"),
    policy_version: string(body.policy_version, "policy_version"),
    analyzed_at: string(body.analyzed_at, "analyzed_at"),
    exported_at: string(body.exported_at, "exported_at"),
    plan_digest: string(body.plan_digest, "plan_digest"),
    retention_notice: string(body.retention_notice, "retention_notice"),
    grants_file_operation: boolean(body.grants_file_operation, "grants_file_operation"),
    read_only_notice: string(body.read_only_notice, "read_only_notice"),
    limits: decodeLimits(body.limits),
    groups: array(body.groups, "groups").map((group) => {
      const item = record(group, "group");
      return {
        group_key: string(item.group_key, "group_key"),
        length: integer(item.length, "length"),
        evidence_hash: string(item.evidence_hash, "evidence_hash"),
        evidence_basis: string(item.evidence_basis, "evidence_basis"),
      };
    }),
    unverified: array(body.unverified, "unverified").map(decodeItem),
    unreadable: array(body.unreadable, "unreadable").map(decodeItem),
    truncated: boolean(body.truncated, "truncated"),
    recheck: {
      performed: boolean(recheck.performed, "performed"),
      status: string(recheck.status, "status"),
      reasons: array(recheck.reasons, "reasons").map((reason) => string(reason, "reason")),
      performed_at: optionalString(recheck.performed_at, "performed_at"),
    },
  };
}

function decodeLimits(value: unknown) {
  const body = record(value, "limits");
  return {
    maximum_files: integer(body.maximum_files, "maximum_files"),
    maximum_bytes: integer(body.maximum_bytes, "maximum_bytes"),
    maximum_file_bytes: integer(body.maximum_file_bytes, "maximum_file_bytes"),
    hash_concurrency: integer(body.hash_concurrency, "hash_concurrency"),
  };
}

function decodeSummary(value: unknown): DedupSummary {
  const body = record(value, "summary");
  return {
    policy_version: string(body.policy_version, "policy_version"),
    analyzed_at: string(body.analyzed_at, "analyzed_at"),
    plan_digest: string(body.plan_digest, "plan_digest"),
    statistics: decodeStatistics(body.statistics),
    plan: decodePlan(body.plan),
    duplicate_group_count: integer(body.duplicate_group_count, "duplicate_group_count"),
    duplicate_file_count: integer(body.duplicate_file_count, "duplicate_file_count"),
    unverified_count: integer(body.unverified_count, "unverified_count"),
    unreadable_count: integer(body.unreadable_count, "unreadable_count"),
    unique_count: integer(body.unique_count, "unique_count"),
    retained_item_count: integer(body.retained_item_count, "retained_item_count"),
    truncated: boolean(body.truncated, "truncated"),
    retention_notice: string(body.retention_notice, "retention_notice"),
    analysis_version: string(body.analysis_version, "analysis_version"),
  };
}

function decodeStatistics(value: unknown): DedupStatistics {
  const body = record(value, "statistics");
  return {
    observed_entries: integer(body.observed_entries, "observed_entries"),
    analyzed_files: integer(body.analyzed_files, "analyzed_files"),
    not_read_files: integer(body.not_read_files, "not_read_files"),
    failed_files: integer(body.failed_files, "failed_files"),
    skipped_files: integer(body.skipped_files, "skipped_files"),
    byte_duplicate_groups: integer(body.byte_duplicate_groups, "byte_duplicate_groups"),
    byte_duplicate_files: integer(body.byte_duplicate_files, "byte_duplicate_files"),
    byte_duplicate_bytes: integer(body.byte_duplicate_bytes, "byte_duplicate_bytes"),
    read_bytes: integer(body.read_bytes, "read_bytes"),
    additional_read_attempts: integer(body.additional_read_attempts, "additional_read_attempts"),
  };
}

function decodePlan(value: unknown): DedupPlanStatement {
  const body = record(value, "plan");
  return {
    status: string(body.status, "status"),
    status_text: string(body.status_text, "status_text"),
    unreadable_paths: paths(body.unreadable_paths),
    incomplete_reason_count: integer(body.incomplete_reason_count, "incomplete_reason_count"),
    scan_bounds_reached: boolean(body.scan_bounds_reached, "scan_bounds_reached"),
    failure_code: optionalString(body.failure_code, "failure_code"),
    source_failures: array(body.source_failures, "source_failures").map((failure) => {
      const item = record(failure, "source failure");
      return { source_id: string(item.source_id, "source_id"), reason_code: string(item.reason_code, "reason_code") };
    }),
  };
}

function decodeGroup(value: unknown): DedupGroup {
  const body = record(value, "group");
  return {
    group_key: string(body.group_key, "group_key"),
    length: integer(body.length, "length"),
    evidence_hash: string(body.evidence_hash, "evidence_hash"),
    member_count: integer(body.member_count, "member_count"),
    identity_merge_proposed: boolean(body.identity_merge_proposed, "identity_merge_proposed"),
    members: array(body.members, "members").map(decodeItem),
  };
}

function decodeItem(value: unknown): DedupItem {
  const body = record(value, "item");
  return {
    source_id: string(body.source_id, "source_id"),
    root: string(body.root, "root"),
    relative_path: string(body.relative_path, "relative_path"),
    name: string(body.name, "name"),
    length: integer(body.length, "length"),
    sha256: optionalString(body.sha256, "sha256"),
    structure_hash: optionalString(body.structure_hash, "structure_hash"),
    last_write_time_utc: string(body.last_write_time_utc, "last_write_time_utc"),
    state: string(body.state, "state"),
    state_text: string(body.state_text, "state_text"),
    read_state: string(body.read_state, "read_state"),
    read_state_text: string(body.read_state_text, "read_state_text"),
    failure: string(body.failure, "failure"),
    skip_reason: string(body.skip_reason, "skip_reason"),
    group_key: optionalString(body.group_key, "group_key"),
    category: string(body.category, "category"),
    relations: array(body.relations, "relations").map((relation) => string(relation, "relation")),
    relation_notes: array(body.relation_notes, "relation_notes").map((note) => string(note, "relation_note")),
  };
}

function paths(value: unknown): string[] {
  return array(value, "paths").map((path) => string(path, "path"));
}

export function errorMessage(error: unknown): string {
  return error instanceof AssetLinkApiError ? error.message : "请求未被执行，请稍后重试。";
}
