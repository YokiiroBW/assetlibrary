import {
  mockAssetLink,
  mockSession,
  browserSession,
  libraryPage,
  result,
  visibleLibrary,
} from "./assetlink-fixtures.mjs";

const libraryId = visibleLibrary.library_id;
const taskId = "88888888-8888-4888-8888-888888888888";

export const dedupLibrary = {
  ...visibleLibrary,
  access_level: "library_administrator",
};

export function dedupJob(overrides = {}) {
  return {
    task_id: taskId,
    library_id: libraryId,
    library_display_name: dedupLibrary.display_name,
    state: "queued",
    cancellation_requested: false,
    can_cancel: true,
    can_retry: false,
    report_available: false,
    analysis_version: `${taskId}:0`,
    created_at: "2026-09-07T10:00:00Z",
    updated_at: "2026-09-07T10:00:00Z",
    failure_code: null,
    retention_notice: "服务器只保留最近 8 份分析结果；旧结果不保证仍可读取。",
    read_only_notice: "查重只读取文件内容用于计算哈希，不会移动、复制或删除任何文件。",
    ...overrides,
  };
}

function statistics(overrides = {}) {
  return {
    observed_entries: 120,
    analyzed_files: 80,
    not_read_files: 30,
    failed_files: 5,
    skipped_files: 5,
    byte_duplicate_groups: 2,
    byte_duplicate_files: 5,
    byte_duplicate_bytes: 4_194_304,
    read_bytes: 10_485_760,
    additional_read_attempts: 3,
    ...overrides,
  };
}

function plan(overrides = {}) {
  return {
    status: "PartiallyAnalyzed",
    status_text: "本次分析覆盖了预算范围内的文件，但有部分文件未能读取。",
    unreadable_paths: ["broken/one.bin"],
    incomplete_reason_count: 0,
    scan_bounds_reached: false,
    failure_code: null,
    source_failures: [],
    ...overrides,
  };
}

export function dedupSummary(overrides = {}) {
  return {
    policy_version: "dedup.exact.v1",
    analyzed_at: "2026-09-07T10:00:00Z",
    plan_digest: "digest-4f2a9c7b",
    statistics: statistics(),
    plan: plan(),
    duplicate_group_count: 2,
    duplicate_file_count: 5,
    unverified_count: 2,
    unreadable_count: 1,
    unique_count: 40,
    retained_item_count: 8,
    truncated: false,
    retention_notice: "服务器只保留最近 8 份分析结果；旧结果不保证仍可读取。",
    analysis_version: `${taskId}:1`,
    ...overrides,
  };
}

function item(relativePath, overrides = {}) {
  return {
    source_id: "33333333-3333-4333-8333-333333333333",
    root: "C:/fixture-storage",
    relative_path: relativePath,
    name: relativePath.split("/").at(-1),
    length: 2_097_152,
    sha256: "b1946ac92492d2347c6235b4d2611184c3f0d5a9ad0d4b2f6f6f5c4a2d1b0c9e",
    structure_hash: "1234567",
    last_write_time_utc: "2026-09-01T08:00:00Z",
    state: "VerifiedDuplicate",
    state_text: "内容一致的重复文件",
    read_state: "Read",
    read_state_text: "已完整读取",
    failure: "None",
    skip_reason: "None",
    group_key: "group-a",
    category: "Photos",
    relations: ["SameContent"],
    relation_notes: ["与同组文件内容一致"],
    ...overrides,
  };
}

export function dedupGroup(overrides = {}) {
  return {
    group_key: "group-a",
    length: 2_097_152,
    evidence_hash: "b1946ac92492d2347c6235b4d2611184c3f0d5a9ad0d4b2f6f6f5c4a2d1b0c9e",
    member_count: 2,
    identity_merge_proposed: false,
    members: [item("holiday/beach.png"), item("backup/beach.png")],
    ...overrides,
  };
}

export function dedupPage(overrides = {}) {
  return {
    task_id: taskId,
    analysis_version: `${taskId}:1`,
    kind: "ByteDuplicateGroup",
    kind_text: "完整强哈希一致的重复组",
    offset: 0,
    page_size: 50,
    total: 2,
    next_cursor: null,
    report_available: true,
    groups: [dedupGroup()],
    items: [],
    summary: dedupSummary(),
    ...overrides,
  };
}

/**
 * The answer to a start. The library's own operation key asks for whatever the library already has —
 * that is how a page claims a task it did not itself remember — while any other key starts a new
 * analysis, exactly as the server's durable task identity does.
 */
function startAnswer(state, body) {
  if (body.operation_key === libraryId) return state.job;
  state.started = (state.started ?? 0) + 1;
  // A new attempt supersedes the previous version and has no report of its own yet.
  state.job = { ...state.job, state: "queued", report_available: false, can_cancel: true, can_retry: false };
  return state.job;
}

/**
 * The answer to a request for one group's members. The section page is kept in `state.page`; this only
 * changes the section the page describes, exactly like the server's group page does.
 */
function memberPage(state, groupKey) {
  const group = state.page.groups.find((candidate) => candidate.group_key === groupKey);
  if (group === undefined) throw new Error(`The fixture has no group ${groupKey}`);
  // The section listing is rebuilt from the stored page on every request, so answering a group request
  // never consumes the section the page will ask for next.
  return { ...state.page, groups: [], items: group.members, total: group.members.length };
}

/**
 * A dedup workbench fixture. It answers only the six authorized operations and records every request so
 * a test can assert what the page sent — including that it never sends a filesystem path.
 */
export async function mockDedup(page, options = {}) {
  const state = {
    job: options.job ?? dedupJob(),
    page: options.page ?? dedupPage(),
    recheck: {
      task_id: taskId,
      status: "PlanCurrent",
      status_text: "当前计划仍然有效。",
      plan_still_current: true,
      reasons: [],
      changed_paths: [],
      disappeared_paths: [],
      new_paths: [],
      plan_digest: "digest-4f2a9c7b",
      previous_plan_digest: "digest-4f2a9c7b",
      analysis_version: `${taskId}:1`,
      report_available: true,
      retention_notice: "服务器只保留最近 8 份分析结果；旧结果不保证仍可读取。",
    },
    requests: [],
    failure: null,
  };
  await mockSession(page, { ...browserSession, is_system_administrator: true, display_name: "管理员" });
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request, [dedupLibrary]);
    if (request.operation === "libraries.get") return result(request, { library: dedupLibrary });
    if (request.operation === "library_scans.get")
      return result(request, {
        library_id: libraryId,
        scan: { task_id: taskId, state: "succeeded", cancellation_requested: false, observed_entries: 1 },
      });
    if (request.operation === "entries.browse")
      return result(request, { library: dedupLibrary, parent_relative_path: "", items: [], next_cursor: null });
    if (request.operation === "assets.search") return result(request, { items: [], next_cursor: null });
    throw new Error(`Unexpected control operation in dedup fixture: ${request.operation}`);
  });
  await page.route("**/assetlink/v1/dedup/*", async (route) => {
    const operation = route.request().url().split("/").at(-1);
    const body = route.request().postDataJSON();
    if (!route.request().headers()["x-assetlibrary-csrf"]) throw new Error("A memory CSRF token is required");
    state.requests.push({ operation, body });
    if (state.failure !== null) {
      const { status, code, message } = state.failure;
      await route.fulfill({
        status,
        contentType: "application/json",
        body: JSON.stringify({ error: { code, message }, status }),
      });
      return;
    }
    const answer = () => {
      if (operation === "start") return startAnswer(state, body);
      if (operation === "status" || operation === "cancel") return state.job;
      // A group request answers with that group's members, never with the section listing: the two are
      // different shapes, which is why the server answers one or the other and never both.
      if (operation === "results") return body.group_key === undefined ? state.page : memberPage(state, body.group_key);
      if (operation === "revalidate") return state.recheck;
      if (operation === "export")
        return {
          format_version: 1,
          document_type: "dedup.plan",
          task_id: taskId,
          analysis_version: `${taskId}:1`,
          library_id: libraryId,
          library_display_name: dedupLibrary.display_name,
          policy_version: "dedup.exact.v1",
          analyzed_at: "2026-09-07T10:00:00Z",
          exported_at: "2026-09-07T10:05:00Z",
          plan_digest: "digest-4f2a9c7b",
          retention_notice: "服务器只保留最近 8 份分析结果；旧结果不保证仍可读取。",
          grants_file_operation: false,
          read_only_notice: "查重只读取文件内容用于计算哈希，不会移动、复制或删除任何文件。",
          limits: {
            maximum_files: 200000,
            maximum_bytes: 2147483648,
            maximum_file_bytes: 268435456,
            hash_concurrency: 4,
          },
          groups: [
            {
              group_key: "group-a",
              length: 2_097_152,
              evidence_hash: "b1946ac92492d2347c6235b4d2611184c3f0d5a9ad0d4b2f6f6f5c4a2d1b0c9e",
              evidence_basis: "完整强哈希一致",
            },
          ],
          unverified: [],
          unreadable: [],
          truncated: false,
          recheck: { performed: false, status: "NotPerformed", reasons: [], performed_at: null },
        };
      throw new Error(`Unexpected dedup operation: ${operation}`);
    };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(answer()) });
  });
  return state;
}
