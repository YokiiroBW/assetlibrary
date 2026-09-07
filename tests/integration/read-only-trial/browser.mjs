import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

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
try {
  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
    ignoreHTTPSErrors: false,
  });
  page = await context.newPage();
  await page.goto(settings.origin);
  await expect(page.getByRole("heading", { name: "登录资源库" })).toBeVisible();
  await page.getByLabel("账号", { exact: true }).fill(settings.account_name);
  await page.getByLabel("密码", { exact: true }).fill(settings.password);
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(page.getByRole("button", { name: "退出登录", exact: true })).toBeVisible();

  let libraryId = settings.library_id ?? null;
  if (settings.phase === "initial") {
    await page.goto(new URL("/libraries", settings.origin).href);
    await page.getByRole("button", { name: "添加资源库", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "添加资源库" });
    await expect(dialog.getByLabel("存储源", { exact: true })).toBeEnabled();
    await dialog.getByLabel("资源库名称", { exact: true }).fill("真实只读试用");
    await dialog.getByLabel("资源库分类", { exact: true }).selectOption("images");
    await dialog.getByRole("checkbox", { name: "高级：指定该范围内的目录", exact: true }).check();
    await dialog.getByLabel("服务器目录", { exact: true }).fill(settings.library_root);
    const registration = page.waitForResponse(
      (response) =>
        response.url().endsWith("/assetlink/v1/control") &&
        response.request().postDataJSON()?.operation === "libraries.register",
    );
    await dialog.getByRole("button", { name: "添加资源库", exact: true }).click();
    const registered = await registration;
    assert.equal(registered.status(), 200, `registration status ${registered.status()}`);
    libraryId = (await registered.json()).body.library_id;
    await expect(dialog).toHaveCount(0);
    await page.goto(new URL(`/tasks?library=${libraryId}`, settings.origin).href);
    await expect(page.getByText("尚未建立首次索引", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "开始首次扫描", exact: true }).click();
    await expect(page.getByText("首次扫描已完成", { exact: true })).toBeVisible({ timeout: 60000 });
    await page.getByRole("link", { name: "浏览资源库", exact: true }).click();
    const album = page.getByRole("option", { name: /^album，/ });
    await expect(album).toBeVisible();
    await album.dblclick();
    await expect.poll(() => new URL(page.url()).searchParams.get("path")).toBe("album");
    await page.getByRole("option", { name: /^summer-photo\.jpg，/ }).dblclick();
    await expect(page.getByRole("complementary", { name: "资产详情" })).toContainText("summer-photo.jpg");
    await expect.poll(() => new URL(page.url()).searchParams.has("entry")).toBe(true);
    await page.goBack();
    await expect.poll(() => new URL(page.url()).searchParams.has("entry")).toBe(false);
    await page.reload();
    await expect(page.getByRole("option", { name: /^summer-photo\.jpg，/ })).toBeVisible();
    await page.getByRole("navigation", { name: "面包屑" }).getByRole("link", { name: "真实只读试用", exact: true }).click();
    await expect(page.getByRole("option", { name: /^album，/ })).toBeVisible();
  } else {
    await expect(page.getByText("真实只读试用", { exact: true }).first()).toBeVisible();
  }

  await page.getByPlaceholder("搜索文件名或相对路径").fill("album/summer-photo.jpg");
  const resultEntry = page.getByRole("option", { name: /^summer-photo\.jpg，/ });
  await expect(resultEntry).toBeVisible({ timeout: 15000 });
  await page.reload();
  await expect(resultEntry).toBeVisible();
  await page.screenshot({ path: join(settings.evidence, `${settings.phase}-desktop.png`), animations: "disabled" });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ colorScheme: "dark", reducedMotion: "reduce" });
  await expect(resultEntry).toBeVisible();
  await resultEntry.dblclick();
  const details = page.getByRole("dialog", { name: "资产详情", exact: true });
  await expect(details).toContainText("summer-photo.jpg");
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  assert.equal(await page.evaluate(() => localStorage.length + sessionStorage.length), 0);
  await page.screenshot({
    path: join(settings.evidence, `${settings.phase}-mobile-dark.png`),
    animations: "disabled",
    fullPage: true,
  });
  await details.getByRole("button", { name: "关闭资产详情", exact: true }).click();
  await expect(details).toHaveCount(0);
  const result = { status: "passed", phase: settings.phase, library_id: libraryId, browser_version: browser.version() };
  await writeFile(join(settings.evidence, `${settings.phase}-browser.json`), JSON.stringify(result, null, 2));
  process.stdout.write(JSON.stringify(result));
} catch (error) {
  if (page)
    await page
      .screenshot({ path: join(settings.evidence, `${settings.phase}-failure.png`), fullPage: true })
      .catch(() => process.stderr.write("screenshot_unavailable\n"));
  process.stderr.write(
    String(error.message)
      .replaceAll(settings.password, "[redacted]")
      .replaceAll(settings.library_root, "[fixture-library]"),
  );
  process.exitCode = 1;
} finally {
  await browser.close();
}
