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

/** The report version the first analysis of this fixture publishes, and the one a recheck replaces it with. */
export const firstVersion = `${taskId}:1`;
export const secondVersion = `${taskId}:2`;
export const firstDigest = "digest-4f2a9c7b";
export const secondDigest = "digest-9c1e0d24";

/** A second registered library, so a test can switch away from the one whose answer is on the wire. */
export const secondLibrary = {
  ...visibleLibrary,
  library_id: "44444444-4444-4444-8444-444444444444",
  display_name: "归档素材",
  access_level: "library_administrator",
};

export const dedupLibrary = {
  ...visibleLibrary,
  access_level: "library_administrator",
};

/**
 * The budget the server states for a run. The page renders these numbers instead of its own, so the
 * fixture carries them on the job exactly as the Host does.
 */
export function dedupLimits(overrides = {}) {
  return {
    maximum_files: 200_000,
    maximum_bytes: 2_147_483_648,
    maximum_file_bytes: 268_435_456,
    hash_concurrency: 4,
    ...overrides,
  };
}

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
    analysis_version: firstVersion,
    created_at: "2026-09-07T10:00:00Z",
    updated_at: "2026-09-07T10:00:00Z",
    failure_code: null,
    retention_notice: "服务器只保留最近 8 份分析结果；旧结果不保证仍可读取。",
    read_only_notice: "查重只读取文件内容用于计算哈希，不会移动、复制或删除任何文件。",
    limits: dedupLimits(),
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
    plan_digest: firstDigest,
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
    analysis_version: firstVersion,
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
    analysis_version: firstVersion,
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
 * A recheck as the server answers it: a receipt for a pending run, or an outcome once the durable task
 * has one. The page must render the receipt as "still checking" rather than as a clean plan, so the two
 * shapes are built from one body here and only the state differs.
 *
 * A completed recheck has filed the version it produced, so its answer names that version and the digest
 * of the plan it published. The defaults here are the outcome a recheck of an unchanged library produces:
 * a new version of the same task, whose plan is still the one that was verified.
 */
export function dedupRecheck(overrides = {}) {
  const state = overrides.state ?? "completed";
  return {
    state,
    state_text:
      state === "pending"
        ? "复核已在后台排队，完成后结果才会显示。"
        : state === "refused"
          ? "复核未执行，报告版本已不再保留或被取代。"
          : "复核已完成。",
    task_id: taskId,
    recheck_task_id: "99999999-9999-4999-8999-999999999999",
    completed: state === "completed",
    status: state === "completed" ? "PlanCurrent" : "",
    status_text: state === "completed" ? "当前计划仍然有效。" : "",
    plan_still_current: state === "completed",
    reasons: [],
    changed_paths: [],
    disappeared_paths: [],
    new_paths: [],
    plan_digest: state === "completed" ? secondDigest : firstDigest,
    previous_plan_digest: state === "completed" ? firstVersion : null,
    analysis_version: state === "completed" ? secondVersion : "",
    failure_code: state === "refused" ? "dedup_report_not_retained" : null,
    report_available: state === "completed",
    retention_notice: "服务器只保留最近 8 份分析结果；旧结果不保证仍可读取。",
    read_only_notice: "查重只读取文件内容用于计算哈希，不会移动、复制或删除任何文件。",
    ...overrides,
  };
}

/** The receipt the server answers a filed recheck with: the run exists, its outcome does not yet. */
function recheckReceipt(body) {
  return dedupRecheck({
    state: "pending",
    completed: false,
    status: "",
    status_text: "",
    plan_still_current: false,
    plan_digest: body.plan_digest,
    previous_plan_digest: null,
    analysis_version: "",
    failure_code: null,
    report_available: false,
  });
}

/**
 * Files the version a completed recheck produced, the way the server does: the same task, the next
 * version, the digest of the plan that version holds, and a receipt for the run that names them. A test
 * that wants the page to see the version move calls this, because the fixture's answers are built from
 * the server's own state: a receipt that named a version the server had not filed would be a server the
 * fixture does not model.
 */
function publishRecheck(state, digest = secondDigest) {
  state.server = { version: secondVersion, digest };
  state.page = {
    ...state.page,
    analysis_version: secondVersion,
    summary: { ...state.page.summary, analysis_version: secondVersion, plan_digest: digest },
  };
  state.job = { ...state.job, analysis_version: secondVersion };
  state.recheck = dedupRecheck({ plan_digest: digest });
}

/**
 * A dedup workbench fixture. It answers only the six authorized operations and records every request so
 * a test can assert what the page sent — including that it never sends a filesystem path.
 *
 * The fixture keeps the server's own version and digest in `state.server`, and every answer is built from
 * them, so a page that names a version the server no longer holds is refused exactly as the real Core
 * refuses it. `state.beforeAnswer` lets a test hold one answer open — a slow read or a slow status — which
 * is how the page's behaviour while an answer is in flight is exercised.
 */
export async function mockDedup(page, options = {}) {
  const server = { version: firstVersion, digest: firstDigest };
  const state = {
    job: options.job ?? dedupJob(),
    page: options.page ?? dedupPage(),
    // The answer a poll of the recheck's task receives. It is pending by default because that is the run
    // the page has to follow; a test moves it forward by filing the version the run produced.
    recheck: options.recheck ?? dedupRecheck({ state: "pending" }),
    server,
    requests: [],
    failure: null,
    beforeAnswer: null,
  };
  // What a completed recheck does to the server: it files the version it produced and the run's answer
  // becomes that outcome. A test that wants the version to move mid-flow calls this, which is the only
  // way the page can be made to follow it.
  state.publish = (digest) => publishRecheck(state, digest);
  // The answers are built from what the server holds now rather than from what the test passed in, so a
  // test can move the server forward and see the page follow it.
  const pageNow = (body) => {
    // A page is answered for the library that asked: the fixture holds one library's report, and a second
    // library is only ever used to prove the page does not carry one library's answer into another.
    if (body.library_id !== libraryId) {
      return {
        ...state.page,
        task_id: body.task_id,
        total: 0,
        groups: [],
        items: [],
        next_cursor: null,
      };
    }
    return body.group_key === undefined ? state.page : memberPage(state, body.group_key);
  };
  const planNow = () => ({
    format_version: 1,
    document_type: "dedup.plan",
    task_id: taskId,
    analysis_version: state.server.version,
    library_id: libraryId,
    library_display_name: dedupLibrary.display_name,
    policy_version: "dedup.exact.v1",
    analyzed_at: "2026-09-07T10:00:00Z",
    exported_at: "2026-09-07T10:05:00Z",
    plan_digest: state.server.digest,
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
  });
  await mockSession(page, { ...browserSession, is_system_administrator: true, display_name: "管理员" });
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request, options.libraries ?? [dedupLibrary]);
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
    if (state.beforeAnswer !== null) await state.beforeAnswer({ operation, body, state });
    // A failure can be aimed at one operation or at every one of them. Aiming it at one is how a test
    // exercises a refused export while the page is still following a recheck it also has to answer.
    const refused =
      state.failure === null ? null : (state.failure[operation] ?? (state.failure.status ? state.failure : null));
    if (refused !== null) {
      await route.fulfill({
        status: refused.status,
        contentType: "application/json",
        body: JSON.stringify({ error: { code: refused.code, message: refused.message }, status: refused.status }),
      });
      return;
    }
    const answer = () => {
      if (operation === "start") return startAnswer(state, body);
      if (operation === "status" || operation === "cancel") return state.job;
      // A group request answers with that group's members, never with the section listing: the two are
      // different shapes, which is why the server answers one or the other and never both.
      if (operation === "results") return pageNow(body);
      if (operation === "revalidate") {
        // A request that names the recheck's task polls that run; one that names none files it. Filing
        // answers with a receipt, and the poll answers with the outcome, which is how the page learns
        // the background comparison finished without ever holding a request open for it.
        return body.recheck_task_id === undefined ? recheckReceipt(body) : state.recheck;
      }
      if (operation === "export") {
        // The Core refuses an export bound to a version or digest it no longer holds rather than
        // re-binding it, so the fixture does too: a page that exports a superseded version is told so.
        if (body.analysis_version !== state.server.version || body.plan_digest !== state.server.digest) {
          return {
            error: { code: "dedup_version_conflict", message: "导出的版本与当前保留的报告版本不一致。" },
            status: 409,
          };
        }
        return planNow();
      }
      throw new Error(`Unexpected dedup operation: ${operation}`);
    };
    const answered = answer();
    if (answered !== undefined && answered.status === 409) {
      await route.fulfill({
        status: 409,
        contentType: "application/json",
        body: JSON.stringify({ error: answered.error, status: 409 }),
      });
      return;
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(answered) });
  });
  return state;
}
