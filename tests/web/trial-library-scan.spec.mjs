import { expect, test } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import { mockTrial } from "./trial-fixtures.mjs";
import { scanSummary } from "./assetlink-fixtures.mjs";

async function registrationForm(page, rootPath = "C:/fixture-storage/photos") {
  await page.getByRole("button", { name: "添加资源库", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "添加资源库" });
  await expect(dialog.getByLabel("存储源", { exact: true })).toBeEnabled();
  await dialog.getByLabel("资源库名称", { exact: true }).fill("试用照片");
  await dialog.getByLabel("服务器目录", { exact: true }).fill(rootPath);
  return dialog;
}

test("administrator registers a controlled source and explicitly scans before browsing real indexed names", async ({
  page,
}, testInfo) => {
  const state = await mockTrial(page, { empty: true });
  state.sources = [{ source_key: "photos", display_name: "试用存储" }];
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "没有可见资源库" })).toBeVisible();
  const dialog = await registrationForm(page, "/assets/photos");
  await expect(dialog.getByLabel("服务器目录", { exact: true })).toHaveAccessibleDescription(
    /部署挂载表.*\/assets\/photos.*Windows 原生/s,
  );
  await expect(dialog.getByRole("option", { name: "试用存储" })).toHaveCount(1);
  await page.screenshot({ path: testInfo.outputPath("register-desktop.png"), animations: "disabled" });
  await dialog.getByRole("button", { name: "添加资源库", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByText("尚未建立首次索引", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "这个目录是空的" })).toHaveCount(0);
  expect(state.requests.filter((request) => request.operation === "library_scans.start")).toHaveLength(0);
  const registration = state.requests.find((request) => request.operation === "libraries.register");
  expect(registration.body).toEqual({
    source_key: "photos",
    display_name: "试用照片",
    root_path: "/assets/photos",
  });
  expect(registration.idempotency_key).toMatch(/^[0-9a-f-]{36}$/);
  await page.getByRole("button", { name: "开始首次扫描" }).click();
  await expect(page.getByText("等待扫描", { exact: true })).toBeVisible();
  state.scan = { ...state.scan, state: "leased", observed_entries: 1234 };
  await expect(page.getByText("正在扫描", { exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "首次扫描" })).toContainText("已发现 1,234 项");
  await page.screenshot({ path: testInfo.outputPath("scan-desktop.png"), animations: "disabled" });
  state.scan = { ...state.scan, state: "succeeded", observed_entries: 1, committed_entries: 1, can_cancel: false };
  await expect(page.getByText("首次扫描已完成", { exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "首次扫描" })).toContainText("刷新不会重新扫描目录");
  await expect(page.getByText("sample-photo.jpg", { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: "开始首次扫描" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "重试扫描" })).toHaveCount(0);
});

test("registration errors preserve the form and retry identity without enumerating server paths", async ({ page }) => {
  const state = await mockTrial(page, { empty: true });
  state.registerFailure = 503;
  await page.goto("/");
  const dialog = await registrationForm(page);
  await dialog.getByRole("button", { name: "添加资源库", exact: true }).click();
  await expect(dialog.getByRole("alert")).toBeFocused();
  await expect(dialog.getByLabel("服务器目录", { exact: true })).toHaveValue("C:/fixture-storage/photos");
  const first = state.requests.find((request) => request.operation === "libraries.register");
  state.registerFailure = null;
  await dialog.getByRole("button", { name: "添加资源库", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  const second = state.requests.filter((request) => request.operation === "libraries.register")[1];
  expect(second.idempotency_key).toBe(first.idempotency_key);
  expect(second.request_id).toBe(first.request_id);
  expect(
    state.requests.every((request) =>
      ["libraries.list", "entries.browse", "storage_sources.list", "libraries.register", "library_scans.get"].includes(
        request.operation,
      ),
    ),
  ).toBeTruthy();
});

test("unconfirmed start and cancel submissions reuse their keys, while a failed attempt gets a new task", async ({
  page,
}) => {
  const state = await mockTrial(page);
  state.scan = null;
  state.startFailure = 503;
  await page.goto("/");
  await page.getByRole("button", { name: "开始首次扫描" }).click();
  await expect(page.getByRole("region", { name: "首次扫描" }).getByRole("alert")).toContainText("尚未确认");
  const firstStart = state.requests.find((request) => request.operation === "library_scans.start");
  state.startFailure = null;
  await page.getByRole("button", { name: "重试提交" }).click();
  await expect(page.getByText("等待扫描", { exact: true })).toBeVisible();
  expect(state.requests.filter((request) => request.operation === "library_scans.start")[1].idempotency_key).toBe(
    firstStart.idempotency_key,
  );
  expect(state.tasksStarted).toBe(1);
  state.cancelFailure = 503;
  await page.getByRole("button", { name: "取消扫描" }).click();
  await expect(page.getByRole("region", { name: "首次扫描" }).getByRole("alert")).toContainText("取消请求");
  const firstCancel = state.requests.find((request) => request.operation === "library_scans.cancel");
  state.cancelFailure = null;
  await page.getByRole("button", { name: "重试提交" }).click();
  await expect(page.getByText("正在取消扫描", { exact: true })).toBeVisible();
  expect(state.requests.filter((request) => request.operation === "library_scans.cancel")[1].idempotency_key).toBe(
    firstCancel.idempotency_key,
  );
  state.scan = { ...state.scan, state: "cancelled", can_cancel: false, can_retry: true };
  await expect(page.getByText("扫描已取消", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "这个目录是空的" })).toHaveCount(0);
  await page.getByRole("button", { name: "重试扫描" }).click();
  await expect(page.getByText("等待扫描", { exact: true })).toBeVisible();
  expect(state.tasksStarted).toBe(2);
  expect(
    state.requests.filter((request) => request.operation === "library_scans.start").at(-1).idempotency_key,
  ).not.toBe(firstStart.idempotency_key);
});

test("reload recovers the active durable scan and hiding the page stops polling", async ({ page }) => {
  const state = await mockTrial(page);
  state.scan = scanSummary({ state: "leased", observed_entries: 10 });
  await page.goto("/");
  await expect(page.getByText("正在扫描", { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("region", { name: "首次扫描" })).toContainText("已发现 10 项");
  expect(state.requests.filter((request) => request.operation === "library_scans.start")).toHaveLength(0);
  await page.evaluate(() => {
    Object.defineProperty(document, "hidden", { configurable: true, value: true });
    document.dispatchEvent(new Event("visibilitychange"));
  });
  const before = state.requests.filter((request) => request.operation === "library_scans.get").length;
  await page.waitForTimeout(2_500);
  expect(state.requests.filter((request) => request.operation === "library_scans.get")).toHaveLength(before);
  state.scan = { ...state.scan, observed_entries: 20 };
  await page.evaluate(() => {
    Object.defineProperty(document, "hidden", { configurable: true, value: false });
    document.dispatchEvent(new Event("visibilitychange"));
  });
  await expect(page.getByRole("region", { name: "首次扫描" })).toContainText("已发现 20 项");
});

test("offline libraries preserve their last index and recover through an explicit refresh", async ({
  page,
}, testInfo) => {
  const state = await mockTrial(page);
  state.library = { ...state.library, availability: "offline" };
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ colorScheme: "dark", reducedMotion: "reduce" });
  await page.goto("/");
  await expect(page.getByText("sample-photo.jpg", { exact: true }).first()).toBeVisible();
  await expect(page.getByText(/此资源库暂时离线/)).toBeVisible();
  await expect(page.getByRole("heading", { name: "这个目录是空的" })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.screenshot({
    path: testInfo.outputPath("offline-narrow-dark.png"),
    animations: "disabled",
    fullPage: true,
  });
  state.library = { ...state.library, availability: "online" };
  await page.getByRole("button", { name: "刷新", exact: true }).click();
  await expect(page.getByText(/此资源库暂时离线/)).toHaveCount(0);
  await expect(page.getByText("sample-photo.jpg", { exact: true }).first()).toBeVisible();
});

test("scan denial clears selected entries and malformed progress never becomes an empty success", async ({ page }) => {
  const state = await mockTrial(page);
  state.browseCursor = "private-next";
  await page.goto("/");
  await page.getByRole("button", { name: "sample-photo.jpg sample-photo.jpg" }).click();
  await expect(page.locator(".detail-pane")).toContainText("sample-photo.jpg");
  state.scanFailure = 403;
  await page.getByRole("button", { name: "刷新", exact: true }).click();
  await expect(page.getByText("扫描状态需要确认", { exact: true })).toBeVisible();
  await expect(page.locator("[data-entry-row]")).toHaveCount(0);
  await expect(page.locator(".detail-pane")).not.toContainText("sample-photo.jpg");
  await expect(page.getByRole("button", { name: "载入更多", exact: true })).toHaveCount(0);
  state.scanFailure = null;
  state.scan = scanSummary({ observed_entries: -1 });
  await page.getByRole("button", { name: "刷新扫描状态" }).click();
  await expect(page.getByText("扫描状态需要确认", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "这个目录是空的" })).toHaveCount(0);
});

test("registration dialog keeps keyboard focus contained and restores it when closed", async ({ page }) => {
  await mockTrial(page, { empty: true });
  await page.goto("/");
  const open = page.getByRole("button", { name: "添加资源库", exact: true });
  await open.focus();
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog", { name: "添加资源库" });
  await expect(dialog.getByLabel("资源库名称", { exact: true })).toBeFocused();
  for (let index = 0; index < 8; index++) {
    await page.keyboard.press("Tab");
    expect(await dialog.evaluate((element) => element.contains(document.activeElement))).toBeTruthy();
  }
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  await expect(open).toBeFocused();
});

test("source list is bounded and does not accept arbitrary storage definitions", async ({ page }) => {
  const state = await mockTrial(page, { empty: true });
  state.sources = Array.from({ length: 33 }, (_, index) => ({
    source_key: `source-${index}`,
    display_name: `存储 ${index}`,
  }));
  await page.goto("/");
  await page.getByRole("button", { name: "添加资源库", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "添加资源库" });
  await expect(dialog.getByRole("alert")).toBeVisible();
  await expect(dialog.getByRole("button", { name: "添加资源库", exact: true })).toBeDisabled();
  expect(state.requests.filter((request) => request.operation === "libraries.register")).toHaveLength(0);
});
