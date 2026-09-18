import assert from "node:assert/strict";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

/**
 * Walks the exact-duplicate workbench in a real Chromium against the real Core over HTTPS. Nothing is
 * mocked: every request the page makes goes to the trial host, which is serving the built Web artifacts
 * and the real AssetLink surface with a real administrator session behind it.
 *
 * The two libraries it is given are what make both halves reachable. The first is analyzed, reviewed,
 * rechecked and exported. The second is a different library, so the page mints a new operation key for it
 * and its start is a genuinely new durable task — which is what allows a cancellation of work in progress
 * rather than a cancellation the server has nothing left to stop.
 */

let input = "";
for await (const chunk of process.stdin) {
  input += chunk;
  assert.ok(input.length <= 16384, "bounded browser input");
}
const settings = JSON.parse(input);
const { chromium, expect } = await import(pathToFileURL(settings.playwright_module).href);
await mkdir(settings.evidence, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  args: [`--ignore-certificate-errors-spki-list=${settings.spki}`],
});
let page;
const operations = [];
const failures = [];
try {
  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
    ignoreHTTPSErrors: false,
    acceptDownloads: true,
  });
  page = await context.newPage();

  // Every operation the page performs is recorded from the wire, so the result can state that the page
  // really called the Core instead of a route handler installed by this script. Console errors and
  // refused responses are collected too, because a page that reports a failure in its own words would
  // otherwise leave the trial guessing why a step did not happen.
  page.on("request", (request) => {
    const url = new URL(request.url());
    if (url.pathname.startsWith("/assetlink/v1/dedup/")) operations.push(url.pathname);
  });
  page.on("console", (message) => {
    if (message.type() === "error") failures.push(`console: ${message.text()}`);
  });
  page.on("pageerror", (error) => failures.push(`pageerror: ${error.message}`));
  page.on("response", (response) => {
    const url = new URL(response.url());
    if (url.pathname.startsWith("/assetlink/v1/dedup/") && response.status() >= 400) {
      failures.push(`response: ${response.status()} ${url.pathname}`);
    }
  });

  // The export hands the document to the browser as an object URL. This records the URL the page creates
  // — and leaves it alive, which only keeps a blob in memory a moment longer — so the trial can read the
  // very bytes the page was about to save. Nothing about the page's own behaviour is replaced: the click
  // below is still the page's own button, and the request behind it still goes to the real host.
  await page.addInitScript(() => {
    const create = URL.createObjectURL.bind(URL);
    const revoke = URL.revokeObjectURL.bind(URL);
    URL.createObjectURL = (blob) => {
      const url = create(blob);
      globalThis.__exportedPlanUrls = [...(globalThis.__exportedPlanUrls ?? []), url];
      return url;
    };
    URL.revokeObjectURL = (url) => {
      if (!(globalThis.__exportedPlanUrls ?? []).includes(url)) revoke(url);
    };
  });

  await page.goto(settings.origin);
  await expect(page.getByRole("heading", { name: "登录资源库" })).toBeVisible();
  await page.getByLabel("账号", { exact: true }).fill(settings.account_name);
  await page.getByLabel("密码", { exact: true }).fill(settings.password);
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(page.getByRole("button", { name: "退出登录", exact: true })).toBeVisible();

  // 1. the administrator opens the workbench and chooses the library from the registered list.
  await page.goto(new URL(`/dedup?library=${settings.library_id}`, settings.origin).href);
  await expect(page.getByRole("heading", { name: "精确查重", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "分析来源与预算", exact: true })).toBeVisible();
  await expect(page.locator("#dedup-library")).toHaveValue(settings.library_id);
  await expect(page.getByText("尚未开始分析：预算由服务器在其自身上限内确定")).toBeVisible();

  // 2. start: the page never begins an analysis by itself, so the button is the only thing that can.
  await page.getByRole("button", { name: "开始分析", exact: true }).click();
  const status = page.locator(".dedup-status");
  await expect(status).toBeVisible({ timeout: 20000 });
  const state = status.locator("dl.dedup-facts").first().locator("dd").first();
  await expect(state).toHaveText(/等待执行|正在分析|分析完成/, { timeout: 20000 });
  // The budget on screen is the one the server accepted for this run, not a number the page invented.
  await expect(page.locator(".dedup-budget")).toBeVisible({ timeout: 20000 });

  // 3. status and results: the findings appear only once the server says a report exists.
  await expect(page.getByRole("heading", { name: "分析结果", exact: true })).toBeVisible({ timeout: 90000 });
  await expect(page.getByRole("region", { name: "重复组结果" })).toBeVisible();
  const group = page.locator(".dedup-group-row").first();
  await expect(group).toBeVisible();
  await expect(group).toContainText("2 个文件");
  await expect(page.locator(".dedup-statistics .dedup-note").first()).toContainText("结果版本");

  // Opening a group shows its members, and closing it returns focus to the row that opened it.
  await group.click();
  const detail = page.locator(".dedup-detail");
  await expect(detail).toBeVisible();
  await expect(detail).toContainText("重复组详情");
  await expect(detail).toContainText("original.bin");
  await expect(detail).toContainText("copy.bin");
  await page.getByRole("button", { name: "关闭重复组详情", exact: true }).click();
  await expect(detail).toHaveCount(0);
  await expect(group).toBeFocused();

  // 4. export: the document the page saves is the one the server produced for the version on screen.
  //    It is exported before the recheck rather than after, and not by preference: filing a recheck makes
  //    the server file that recheck's own report as a new version of the library's result, so a page that
  //    still holds the version it read would export a superseded version and be refused. The page's own
  //    recheck is therefore the step that follows the document it was verifying.
  const exporting = page.waitForEvent("download", { timeout: 60000 }).catch(() => null);
  await page.getByRole("button", { name: "导出计划", exact: true }).click();
  const receipt = page.locator(".dedup-followup [role=status]").filter({ hasText: "已导出结果版本" });
  await expect(receipt).toBeVisible({ timeout: 30000 });
  const saved = await exporting;
  const planPath = join(settings.evidence, "dedup-plan-exported.json");
  if (saved !== null) {
    await saved.saveAs(planPath);
  } else {
    // The page saves through an anchor it never attaches, which Chromium accepts without raising a
    // download event. The document itself is what the trial is about, so it is read from the object URL
    // the page created rather than treated as unexported.
    const text = await page.evaluate(async () => {
      const url = (globalThis.__exportedPlanUrls ?? []).at(-1);
      return url === undefined ? null : await (await fetch(url)).text();
    });
    assert.ok(text, "the page must have produced the exported plan document");
    await writeFile(planPath, text);
  }

  const plan = JSON.parse(await readFile(planPath, "utf8"));
  assert.equal(plan.grants_file_operation, false, "an exported plan grants no file operation");
  assert.equal(plan.library_id, settings.library_id, "the exported plan is the one for this library");
  const version = plan.analysis_version;
  assert.ok(version, "the exported plan names the version it exports");
  assert.ok(
    await page.locator(".dedup-followup").innerText().then((text) => text.includes(version)),
    `the page must report the version it exported: ${version}`,
  );

  // 5. recheck: filed as its own durable task, so the page reports its own outcome rather than guessing
  //    one, and it says the plan it compared is still current.
  await page.getByRole("button", { name: "重新核对", exact: true }).click();
  // Both the export receipt and the recheck outcome announce themselves as status, so the recheck is
  // waited for by its own heading rather than by being the first one on the page.
  const recheck = page.locator(".dedup-followup [role=status]").filter({ hasText: "核对结果" });
  await expect(recheck).toBeVisible({ timeout: 90000 });
  await expect(recheck).toContainText("来源未变化", { timeout: 90000 });

  // 6. cancel: a second library, so this start is a new durable task rather than the finished one, and
  //    the request lands while the worker is still holding it.
  await page.goto(new URL(`/dedup?library=${settings.second_library_id}`, settings.origin).href);
  await expect(page.locator("#dedup-library")).toHaveValue(settings.second_library_id);
  await page.getByRole("button", { name: "开始分析", exact: true }).click();
  const second = page.locator(".dedup-status");
  await expect(second).toBeVisible({ timeout: 20000 });
  await expect(second.locator("dl.dedup-facts").first().locator("dd").first()).toHaveText(/等待执行|正在分析/, {
    timeout: 20000,
  });
  const cancel = page.getByRole("button", { name: "取消分析", exact: true });
  await expect(cancel).toBeEnabled({ timeout: 20000 });
  await cancel.click();
  await expect(second.locator("dl.dedup-facts").first().locator("dd").first()).toHaveText("已取消", { timeout: 60000 });
  await expect(page.getByText("本次分析没有可读取的结果", { exact: false })).toBeVisible({ timeout: 20000 });
  await page.screenshot({ path: join(settings.evidence, "dedup-browser-cancelled.png"), animations: "disabled" });

  // The page keeps nothing in browser storage, and it never wrote to the source.
  assert.equal(await page.evaluate(() => localStorage.length + sessionStorage.length), 0);
  const observed = [...new Set(operations)].sort();
  for (const operation of ["start", "status", "results", "cancel", "revalidate", "export"]) {
    assert.ok(observed.includes(`/assetlink/v1/dedup/${operation}`), `the page must really call ${operation}`);
  }

  const result = {
    status: "passed",
    library_id: settings.library_id,
    second_library_id: settings.second_library_id,
    operations: observed,
    browser_version: browser.version(),
  };
  await writeFile(join(settings.evidence, "dedup-browser.json"), JSON.stringify(result, null, 2));
  process.stdout.write(JSON.stringify(result));
} catch (error) {
  if (page) {
    await page
      .screenshot({ path: join(settings.evidence, "dedup-browser-failure.png"), fullPage: true })
      .catch(() => process.stderr.write("screenshot_unavailable\n"));
    const status = await page
      .locator(".dedup-status, .dedup-followup")
      .allInnerTexts()
      .catch(() => []);
    process.stderr.write(`\nPAGE_STATE ${JSON.stringify(status)}\nFAILURES ${JSON.stringify(failures)}\n`);
  }
  process.stderr.write(
    String(error.message)
      .replaceAll(settings.password, "[redacted]")
      .replaceAll(settings.library_root, "[fixture-library]")
      .replaceAll(settings.second_library_root, "[fixture-library]"),
  );
  process.exitCode = 1;
} finally {
  await browser.close();
}
