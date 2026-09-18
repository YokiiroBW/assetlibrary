import { expect, test } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import {
  dedupGroup,
  dedupJob,
  dedupLibrary,
  dedupPage,
  dedupRecheck,
  dedupSummary,
  firstDigest,
  firstVersion,
  mockDedup,
  secondDigest,
  secondLibrary,
  secondVersion,
} from "./dedup-fixtures.mjs";

const dedupPath = "/dedup";
const libraryId = dedupLibrary.library_id;
/** The report version a completed run publishes: the task identity plus its generation. */
const reportVersion = firstVersion;

/**
 * Holds the answer to one operation open until the returned function is called. It is how a test can act
 * while a read or a status poll is still on the wire, which is the only moment some of the page's rules
 * are observable at all.
 */
function holdAnswers(state, operations) {
  let release = () => {};
  const held = new Promise((resolve) => {
    release = resolve;
  });
  state.beforeAnswer = async ({ operation }) => {
    if (operations.includes(operation)) await held;
  };
  return () => {
    state.beforeAnswer = null;
    release();
  };
}

/**
 * Walks to the workbench and starts one run. Every test needs the same three steps, so they are shared
 * here instead of being repeated as a different sequence per test.
 */
async function startAnalysis(page) {
  await page.goto(dedupPath);
  await page.getByLabel("已登记资源库").selectOption(libraryId);
  await expect(page.getByRole("button", { name: "开始分析" })).toBeEnabled();
  await page.getByRole("button", { name: "开始分析" }).click();
}

test("the workbench states its scope and budget before any analysis is started", async ({ page }, testInfo) => {
  const state = await mockDedup(page);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(dedupPath);
  await expect(page.getByRole("heading", { name: "精确查重", exact: true })).toBeVisible();
  await expect(page.getByText(/不会移动、复制或删除任何文件/).first()).toBeVisible();
  await expect(page.getByText(/结果不代表其他资源库/)).toBeVisible();
  await expect(page.getByRole("heading", { name: "分析来源与预算" })).toBeVisible();
  // Before a run exists the page states no ceiling of its own: it says the server determines the budget
  // rather than printing a number nothing enforces.
  await expect(page.getByText(/预算由服务器在其自身上限内确定/)).toBeVisible();
  await expect(page.getByText("文件数上限")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "开始分析" })).toBeDisabled();
  expect(state.requests).toHaveLength(0);
  await page.getByLabel("已登记资源库").selectOption(libraryId);
  await expect(page.getByRole("button", { name: "开始分析" })).toBeEnabled();
  await page.screenshot({ path: testInfo.outputPath("dedup-idle-1440.png"), animations: "disabled", fullPage: true });
});

test("starting analysis shows the server's own state and never fabricates a result", async ({ page }, testInfo) => {
  const state = await mockDedup(page, { job: dedupJob({ state: "queued" }) });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await startAnalysis(page);
  await expect(page.getByText("等待执行", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "重复组" })).toHaveCount(0);
  const start = state.requests.find((request) => request.operation === "start");
  expect(start.body).toEqual({
    library_id: "11111111-1111-4111-8111-111111111111",
    operation_key: expect.stringMatching(/^[0-9a-f-]{36}$/),
    retry: false,
  });
  // No request may ever carry a filesystem path: the server resolves the root itself.
  expect(JSON.stringify(state.requests)).not.toContain("C:/");
  // The numbers the page shows are the server's accepted budget, not a ceiling the page made up: the
  // first start states none, and this one shows what the server answered with.
  await expect(page.getByText("文件数上限")).toBeVisible();
  await expect(page.getByText("256.0 MB")).toBeVisible();
  await expect(page.getByText("2.0 GB")).toBeVisible();
  state.job = dedupJob({ state: "leased" });
  await expect(page.getByText("正在分析", { exact: true })).toBeVisible({ timeout: 6_000 });
  await page.screenshot({ path: testInfo.outputPath("dedup-scanning-1440.png"), animations: "disabled" });
});

test("a run of the same library restates the budget the server already accepted", async ({ page }) => {
  const state = await mockDedup(page, { job: dedupJob({ state: "succeeded", report_available: true }) });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  await expect(page.getByText("文件数上限")).toBeVisible();
  // A second press of the same button is only possible once the page is idle again, so the run that
  // restates the budget is the one the reader asks for after seeing a finished version.
  state.job = dedupJob({ state: "failed", report_available: false, can_cancel: false, can_retry: true });
  await expect(page.getByRole("button", { name: "开始分析" })).toBeEnabled({ timeout: 6_000 });
  state.job = dedupJob({ state: "queued", report_available: false });
  await page.getByRole("button", { name: "开始分析" }).click();
  await expect
    .poll(() => state.requests.filter((request) => request.operation === "start").at(-1)?.body.maximum_files)
    .toBe(200_000);
  const restated = state.requests.filter((request) => request.operation === "start").at(-1);
  expect(restated.body.maximum_bytes).toBe(2_147_483_648);
  expect(restated.body.maximum_file_bytes).toBe(268_435_456);
});

test("a complete run lists groups, counts and evidence separately from unverified items", async ({
  page,
}, testInfo) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false, analysis_version: "v1" }),
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await startAnalysis(page);
  await expect(page.getByRole("heading", { name: "分析结果" })).toBeVisible({ timeout: 6_000 });
  await expect(page.getByText(new RegExp(`结果版本 ${reportVersion}`)).first()).toBeVisible();
  await expect(page.getByRole("tab", { name: /重复组/ })).toHaveAttribute("aria-selected", "true");
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible();
  await expect(page.getByText(/强哈希一致/).first()).toBeVisible();
  await expect(page.getByText("本次不可读", { exact: true })).toBeVisible();
  await expect(page.getByText(/本次分析覆盖|未能读取|达到预算上限/).first()).toBeVisible();
  await page.screenshot({
    path: testInfo.outputPath("dedup-results-1440.png"),
    animations: "disabled",
    fullPage: true,
  });
  const results = state.requests.filter((request) => request.operation === "results");
  expect(results).toHaveLength(1);
  expect(results[0].body.kind).toBe("ByteDuplicateGroup");
  expect(results[0].body.task_id).toBe(state.job.task_id);
});

test("opening a group loads its members, states the evidence and closes back to the row", async ({ page }) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
    page: dedupPage(),
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  const row = page.getByRole("button", { name: /2 个文件/ });
  await row.click();
  await expect(page.getByRole("heading", { name: "重复组详情" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "重复组详情" })).toBeFocused();
  await expect(page.locator(".dedup-detail")).toContainText("holiday/beach.png");
  await expect(page.locator(".dedup-detail")).toContainText(/内容相同/);
  const memberPage = state.requests.filter((request) => request.operation === "results").at(-1);
  expect(memberPage.body.group_key).toBe("group-a");
  await page.getByRole("button", { name: "关闭重复组详情" }).click();
  await expect(page.getByRole("heading", { name: "重复组详情" })).toHaveCount(0);
  await expect(row).toBeFocused();
});

test("switching section re-reads that section and keeps the version in the URL", async ({ page }) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  state.page = dedupPage({
    kind: "Unverified",
    kind_text: "本次未验证内容的文件",
    groups: [],
    items: [
      {
        source_id: "33333333-3333-4333-8333-333333333333",
        root: "C:/fixture-storage",
        relative_path: "raw/unknown.bin",
        name: "unknown.bin",
        length: 1024,
        sha256: null,
        structure_hash: null,
        last_write_time_utc: "2026-09-01T08:00:00Z",
        state: "Unverified",
        state_text: "本次未验证内容",
        read_state: "NotRead",
        read_state_text: "本次未读取",
        failure: "None",
        skip_reason: "OverBudget",
        group_key: null,
        category: "Unknown",
        relations: [],
        relation_notes: [],
      },
    ],
    total: 1,
  });
  await page.getByRole("tab", { name: /未验证内容/ }).click();
  await expect(page.getByText("raw/unknown.bin")).toBeVisible();
  await expect(page).toHaveURL(/section=unverified/);
  const last = state.requests.filter((request) => request.operation === "results").at(-1);
  expect(last.body.kind).toBe("Unverified");
});

test("a completed recheck moves the page onto the version it filed, and the export writes that version", async ({
  page,
}) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  await expect(page.getByText(new RegExp(`结果版本 ${firstVersion}`)).first()).toBeVisible();
  const reads = () => state.requests.filter((request) => request.operation === "results");
  expect(reads()).toHaveLength(1);

  // The background comparison finishes and the server files the version it produced.
  state.publish();
  await page.getByRole("button", { name: "重新核对" }).click();
  await expect(page.locator(".dedup-followup [role=status]").filter({ hasText: "核对结果" })).toBeVisible();
  await expect(page.getByText(new RegExp(`结果版本 ${secondVersion}`)).first()).toBeVisible({ timeout: 6_000 });
  await expect(page.getByText(new RegExp(`结果版本 ${secondVersion}`)).first()).toBeVisible();
  // The page re-read the report at the version the server now holds rather than reusing the answer it had.
  expect(reads()).toHaveLength(2);
  expect(reads().at(-1).body.task_id).toBe(state.job.task_id);

  await expect(page.getByRole("button", { name: "导出计划" })).toBeEnabled({ timeout: 6_000 });
  await page.getByRole("button", { name: "导出计划" }).click();
  await expect(page.locator(".dedup-followup [role=status]").filter({ hasText: "已导出结果版本" })).toContainText(
    secondVersion,
  );
  const exported = state.requests.filter((request) => request.operation === "export").at(-1);
  // The export names the version the page is showing and the digest that version holds — the one the page
  // re-read after the recheck, not the digest the recheck was filed with.
  expect(exported.body.analysis_version).toBe(secondVersion);
  expect(exported.body.plan_digest).toBe(secondDigest);
  expect(state.requests.filter((request) => request.operation === "revalidate").at(0).body.plan_digest).toBe(
    firstDigest,
  );
  // No answer in the whole flow was refused: the version the page exported is the one the server holds.
  expect(
    state.requests.some((request) => request.operation === "export" && request.body.plan_digest === firstDigest),
  ).toBeFalsy();
});

test("a recheck that finished before the page asked about it is applied on the first poll", async ({ page }) => {
  // The run is finished and its version filed before the page files the recheck, so the first answer the
  // page gets about the run is already the outcome and no pending state is ever shown to the reader.
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  state.publish();
  await page.getByRole("button", { name: "重新核对" }).click();
  await expect(page.getByText("当前计划仍然有效。")).toBeVisible({ timeout: 6_000 });
  await expect(page.getByText(new RegExp(`结果版本 ${secondVersion}`)).first()).toBeVisible({ timeout: 6_000 });
  await expect(page.getByRole("button", { name: "导出计划" })).toBeEnabled({ timeout: 6_000 });
  await page.getByRole("button", { name: "导出计划" }).click();
  const exported = state.requests.filter((request) => request.operation === "export").at(-1);
  expect(exported.body.analysis_version).toBe(secondVersion);
  expect(exported.body.plan_digest).toBe(secondDigest);
});

test("a refused recheck files no version, so the page keeps the one it holds and can still export it", async ({
  page,
}) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
    recheck: dedupRecheck({ state: "refused", reasons: ["要复核的报告版本已不再保留。"] }),
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  await page.getByRole("button", { name: "重新核对" }).click();
  await expect(page.getByText("复核未执行，报告版本已不再保留或被取代。")).toBeVisible();
  // Nothing was filed, so the version on screen is still the server's current one and is still exportable.
  await expect(page.getByText(new RegExp(`结果版本 ${firstVersion}`)).first()).toBeVisible();
  expect(state.requests.filter((request) => request.operation === "results")).toHaveLength(1);
  await page.getByRole("button", { name: "导出计划" }).click();
  const exported = state.requests.filter((request) => request.operation === "export").at(-1);
  expect(exported.body.analysis_version).toBe(firstVersion);
  expect(exported.body.plan_digest).toBe(firstDigest);
});

test("the export is withheld while the version the server filed is still being read", async ({ page }) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  // Every read is held from here on, so the page is between the version the server filed and its own read
  // of it for as long as the test wants.
  const release = holdAnswers(state, ["results"]);
  state.publish();
  await page.getByRole("button", { name: "重新核对" }).click();
  await expect(page.getByText(/服务器已更新结果版本，正在读取新版本/)).toBeVisible({ timeout: 6_000 });
  const exporting = page.getByRole("button", { name: "导出计划" });
  await expect(exporting).toBeDisabled();
  await expect(exporting).toContainText("导出计划");
  expect(state.requests.some((request) => request.operation === "export")).toBeFalsy();
  release();
  await expect(exporting).toBeEnabled({ timeout: 6_000 });
  await exporting.click();
  const exported = state.requests.filter((request) => request.operation === "export").at(-1);
  expect(exported.body.analysis_version).toBe(secondVersion);
});

test("an export the server refuses for a superseded version is reported instead of silently re-bound", async ({
  page,
}) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  const release = holdAnswers(state, ["results"]);
  state.publish();
  await page.getByRole("button", { name: "重新核对" }).click();
  await expect(page.getByText(/服务器已更新结果版本，正在读取新版本/)).toBeVisible({ timeout: 6_000 });
  // The version moves again while the page is still reading, so the version it ends up holding is one the
  // server no longer has. The server's own version check is what refuses it, and the page must say so
  // rather than present the refused answer as an export.
  state.failure = {
    export: { status: 409, code: "dedup_version_conflict", message: "导出的版本与当前保留的报告版本不一致。" },
  };
  release();
  await expect(page.getByRole("button", { name: "导出计划" })).toBeEnabled({ timeout: 6_000 });
  await page.getByRole("button", { name: "导出计划" }).click();
  await expect(page.getByText("导出的版本与当前保留的报告版本不一致。")).toBeVisible();
  const attempted = state.requests.filter((request) => request.operation === "export").at(-1);
  expect(attempted.body.analysis_version).toBe(secondVersion);
  await expect(page.locator(".dedup-followup [role=status]").filter({ hasText: "已导出结果版本" })).toHaveCount(0);
});

test("an answer for a library the reader has left is not applied to the library now open", async ({ page }) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
    libraries: [dedupLibrary, secondLibrary],
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });

  // The reader switches library while the first library's read is still on the wire. The answer that
  // arrives afterwards belongs to the library that was left, so it must not become the open page.
  const release = holdAnswers(state, ["results"]);
  await page.getByLabel("已登记资源库").selectOption(secondLibrary.library_id);
  await expect(page.locator(".dedup-status")).toHaveCount(0);
  await expect(page.getByText("尚未开始分析")).toBeVisible();
  release();
  await page.waitForTimeout(500);
  await expect(page.locator(".dedup-status")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "分析结果" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toHaveCount(0);
  await expect(page.getByText("尚未开始分析")).toBeVisible();
});

test("a stale plan is reported as stale instead of being exported as current", async ({ page }) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  state.recheck = {
    ...dedupRecheck(),
    status: "PlanStale",
    status_text: "当前计划已过期，建议重新分析。",
    plan_still_current: false,
    reasons: ["源内容已变化"],
    changed_paths: ["holiday/beach.png"],
    disappeared_paths: ["backup/beach.png"],
    new_paths: ["holiday/beach-copy.png"],
  };
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  await page.getByRole("button", { name: "重新核对" }).click();
  await expect(page.getByRole("heading", { name: "核对结果" })).toBeVisible();
  await expect(page.getByText("当前计划已过期，建议重新分析。")).toBeVisible();
  await expect(page.getByText(/请重新分析后再导出/)).toBeVisible();
  await expect(page.getByText(/变化 1 项 · 消失 1 项 · 新增 1 项/)).toBeVisible();
  // Filing a recheck names the plan, and following it names the recheck's own durable task: the page
  // polls the run it filed instead of filing a second one.
  const filed = state.requests.find((request) => request.operation === "revalidate");
  expect(filed.body.plan_digest).toBe(firstDigest);
  expect(filed.body.recheck_task_id).toBeUndefined();
  const polled = state.requests.filter((request) => request.operation === "revalidate").at(-1);
  expect(polled.body.recheck_task_id).toBe("99999999-9999-4999-8999-999999999999");
  expect(state.requests.some((request) => request.operation === "export")).toBeFalsy();
});

test("a recheck that has not finished yet is not shown as a plan with nothing changed", async ({ page }) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  // The background task never produces an outcome in this test, so the page sits with the receipt. It
  // must say so rather than render an empty change list, which would read as a verified plan.
  state.recheck = dedupRecheck({ state: "pending" });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  await page.getByRole("button", { name: "重新核对" }).click();
  await expect(page.getByText("复核已在后台排队，完成后结果才会显示。")).toBeVisible();
  await expect(page.getByText(/现在还没有任何对照结论/)).toBeVisible();
  await expect(page.getByText(/变化 0 项/)).toHaveCount(0);
});

test("a recheck the server refused states that no comparison happened", async ({ page }) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  state.recheck = dedupRecheck({
    state: "refused",
    reasons: ["要复核的报告版本已不再保留。"],
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  await page.getByRole("button", { name: "重新核对" }).click();
  await expect(page.getByText("复核未执行，报告版本已不再保留或被取代。")).toBeVisible();
  await expect(page.getByText("要复核的报告版本已不再保留。")).toBeVisible();
  await expect(page.getByRole("button", { name: "导出计划" })).toBeVisible();
});

test("cancelling is a request about the analysis only and keeps the page honest", async ({ page }, testInfo) => {
  const state = await mockDedup(page, { job: dedupJob({ state: "leased" }) });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await startAnalysis(page);
  await expect(page.getByText("正在分析", { exact: true })).toBeVisible({ timeout: 6_000 });
  state.job = dedupJob({ state: "leased", cancellation_requested: true, can_cancel: false });
  await page.getByRole("button", { name: "取消分析" }).click();
  await expect(page.getByText(/已请求取消，仅取消分析本身/)).toBeVisible();
  await expect(page.getByRole("button", { name: "取消分析" })).toBeDisabled();
  await page.screenshot({
    path: testInfo.outputPath("dedup-cancelled-1440.png"),
    animations: "disabled",
    fullPage: true,
  });
  const cancel = state.requests.find((request) => request.operation === "cancel");
  expect(Object.keys(cancel.body).sort()).toEqual(["library_id", "operation_key"]);
});

test("an incomplete scan states its limits and labels the budget evidence apart from duplicates", async ({
  page,
}, testInfo) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  // The report is prepared before the analysis is requested, because the page reads it as soon as the
  // server says the version has one.
  state.page = dedupPage({
    summary: dedupSummary({
      truncated: true,
      plan: {
        status: "PartiallyAnalyzed",
        status_text: "本次分析达到预算上限，部分文件未被读取。",
        unreadable_paths: ["locked/blocked.bin"],
        incomplete_reason_count: 2,
        scan_bounds_reached: true,
        failure_code: null,
        source_failures: [],
      },
      statistics: {
        observed_entries: 320,
        analyzed_files: 200,
        not_read_files: 115,
        failed_files: 3,
        skipped_files: 2,
        byte_duplicate_groups: 2,
        byte_duplicate_files: 5,
        byte_duplicate_bytes: 4_194_304,
        read_bytes: 2_147_483_648,
        additional_read_attempts: 7,
      },
    }),
  });
  await startAnalysis(page);
  await expect(page.getByRole("heading", { name: "分析结果", exact: true })).toBeVisible({ timeout: 6_000 });
  await expect(page.getByText("本次分析达到预算上限，部分文件未被读取。")).toBeVisible();
  await expect(page.getByText("本次在达到预算上限后停止，未处理的部分没有参与比较。")).toBeVisible();
  await expect(page.getByText("本次不可读", { exact: true })).toBeVisible();
  // A partial run states how much of the budget it used rather than presenting itself as a complete scan.
  await expect(page.getByText(/已读取/)).toBeVisible();
  await expect(page.getByText(/达到预算上限|未能读取/).first()).toBeVisible();
  await page.screenshot({
    path: testInfo.outputPath("dedup-incomplete-1440.png"),
    animations: "disabled",
    fullPage: true,
  });
});

test("opening a group in the wide layout puts its detail beside the list and returns focus on close", async ({
  page,
}, testInfo) => {
  await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await startAnalysis(page);
  const row = page.getByRole("button", { name: /2 个文件/ });
  await expect(row).toBeVisible({ timeout: 6_000 });
  await row.click();
  await expect(page.locator(".dedup-detail")).toContainText("holiday/beach.png");
  // From 1200px the detail is a 360px column next to the list, not a block under it.
  const list = await page.locator(".dedup-results").boundingBox();
  const detail = await page.locator(".dedup-detail").boundingBox();
  expect(detail.x).toBeGreaterThan(list.x + list.width - 1);
  expect(Math.round(detail.width)).toBe(360);
  await page.screenshot({ path: testInfo.outputPath("dedup-detail-1440.png"), animations: "disabled", fullPage: true });
  // Closing returns the reader to the row that opened the detail, and the next section still works.
  await page.getByRole("button", { name: "关闭重复组详情" }).click();
  await expect(page.locator(".dedup-detail")).toHaveCount(0);
  await expect(row).toBeFocused();
});

test("a discarded report is stated as unreadable rather than shown as no duplicates", async ({ page }) => {
  await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: false, can_cancel: false }),
  });
  await startAnalysis(page);
  await expect(page.getByRole("heading", { name: "本次结果已不可读取" })).toBeVisible({ timeout: 6_000 });
  await expect(page.getByText(/旧结果不会以空结果的形式展示/)).toBeVisible();
  await expect(page.getByRole("heading", { name: "重复组" })).toHaveCount(0);
});

test("a denied export is surfaced as a permission failure, not as an empty plan", async ({ page }) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  state.failure = { status: 403, code: "permission_denied", message: "当前请求没有操作权限。" };
  await page.getByRole("button", { name: "导出计划" }).click();
  await expect(page.getByText("当前请求没有操作权限。")).toBeVisible();
  expect(state.requests.filter((request) => request.operation === "export")).toHaveLength(1);
});

test("narrow, dark and reduced-motion layouts stack without horizontal overflow", async ({ page }, testInfo) => {
  await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  for (const viewport of [
    { width: 1024, height: 768, name: "1024" },
    { width: 390, height: 844, name: "390" },
    { width: 320, height: 640, name: "320" },
  ]) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.emulateMedia({ colorScheme: viewport.name === "390" ? "dark" : "light", reducedMotion: "reduce" });
    await startAnalysis(page);
    await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
    // A stacked control keeps its touch height instead of stretching to the width it would have had in a
    // row: a select that inherited the 240px flex basis vertically would be a 240px tall box here.
    const select = await page.getByLabel("已登记资源库").boundingBox();
    expect(select.height).toBeLessThanOrEqual(46);
    expect(select.height).toBeGreaterThanOrEqual(44);
    expect(Math.round(select.width)).toBeLessThanOrEqual(viewport.width);
    await page.screenshot({
      path: testInfo.outputPath(`dedup-${viewport.name}.png`),
      animations: "disabled",
      fullPage: true,
    });
  }
});

test("the workbench overview is captured unscrolled at desktop and phone widths", async ({ page }, testInfo) => {
  const state = await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await startAnalysis(page);
  await expect(page.getByRole("button", { name: /2 个文件/ })).toBeVisible({ timeout: 6_000 });
  // The reader's first screen, without scrolling: the reader must be able to judge the overview before
  // deciding to scroll, so this is what is recorded rather than a full-page stitched image.
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: testInfo.outputPath("dedup-overview-1440.png"), animations: "disabled" });
  await expect(page.getByText(/重新核对只读取本次分析记录的来源/)).toBeVisible();
  for (const width of [390, 320]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 640 });
    await page.evaluate(() => window.scrollTo(0, 0));
    const select = await page.getByLabel("已登记资源库").boundingBox();
    expect(select.height).toBeLessThanOrEqual(46);
    await page.screenshot({ path: testInfo.outputPath(`dedup-overview-${width}.png`), animations: "disabled" });
  }
  expect(
    state.requests.every((request) => request.operation !== "start" || request.body.library_id !== undefined),
  ).toBe(true);
});

test("long Chinese names and keyboard order stay usable", async ({ page }) => {
  const longName = "海边的日落与朋友的合影_二零二六年夏季旅行最终整理版本_请勿删除".repeat(2);
  await mockDedup(page, {
    job: dedupJob({ state: "succeeded", report_available: true, can_cancel: false }),
    page: dedupPage({
      groups: [
        dedupGroup({
          group_key: "group-long",
          members: [
            {
              ...dedupGroup().members[0],
              relative_path: `照片/${longName}.png`,
              name: `${longName}.png`,
            },
          ],
          member_count: 1,
        }),
      ],
    }),
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await startAnalysis(page);
  const row = page.getByRole("button", { name: /1 个文件/ });
  await expect(row).toBeVisible({ timeout: 6_000 });
  await row.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "重复组详情" })).toBeVisible();
  await expect(page.locator(".dedup-detail")).toContainText(longName.slice(0, 20));
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  // Tab order reaches the closing operations in the fixed order the page documents.
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "关闭重复组详情" })).toBeFocused();
});
