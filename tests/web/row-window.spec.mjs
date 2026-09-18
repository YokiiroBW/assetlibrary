import { expect, test } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import {
  browsePage,
  browsePath,
  entry,
  entryDetail,
  libraryDetail,
  libraryPage,
  mockAssetLink,
  mockSession,
  visibleLibrary,
} from "./assetlink-fixtures.mjs";

/**
 * The two long lists compute their own window. These cases exist because that window is stateful: it
 * measures a scroll container, so a hook that wrote state on every render would render forever, and a
 * hook that stopped measuring would leave the window behind after a scroll, a resize, a container swap
 * or a change of row height. Each case therefore watches the browser's own console for the recursive
 * update React reports and then exercises the geometry the window is responsible for.
 */

const recursiveUpdates = "Maximum update depth exceeded";

/**
 * The server bounds a page at 100 entries and the page decoder enforces that bound, so this is the
 * largest list a real answer can carry — and still far more rows than the window renders at once.
 */
function assetPage() {
  return Array.from({ length: 100 }, (_, index) => entry(index + 1));
}

function library(index) {
  const suffix = String(index).padStart(12, "0");
  return {
    ...visibleLibrary,
    library_id: `33333333-3333-4333-8333-${suffix}`,
    display_name: `资源库 ${String(index).padStart(2, "0")}`,
  };
}

/** Fails the case if the page reports a recursive update at any point, not only while it is idle. */
function watchForRecursiveUpdates(page) {
  const seen = [];
  page.on("console", (message) => {
    if (message.text().includes(recursiveUpdates)) seen.push(message.text());
  });
  page.on("pageerror", (error) => {
    if (error.message.includes(recursiveUpdates)) seen.push(error.message);
  });
  return seen;
}

test.beforeEach(async ({ page }) => {
  await mockSession(page);
});

test("an idle asset list renders once and stays still", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const loops = watchForRecursiveUpdates(page);
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request);
    if (request.operation === "entries.browse") return browsePage(request, assetPage());
    throw new Error(`unexpected operation ${request.operation}`);
  });
  await page.goto(browsePath);
  await expect(page.getByRole("listbox", { name: "资产列表" })).toBeVisible();
  const rows = page.locator("[data-entry-row]");
  await expect.poll(() => rows.count()).toBeGreaterThan(0);
  const windowed = await rows.count();
  expect(windowed).toBeLessThan(assetPage().length);
  // Nothing is asked of the page here, so nothing about it may change while it is left alone.
  await page.waitForTimeout(1200);
  expect(await rows.count()).toBe(windowed);
  expect(loops).toEqual([]);
});

test("an idle library list renders once and stays still", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const loops = watchForRecursiveUpdates(page);
  const libraries = Array.from({ length: 80 }, (_, index) => library(index + 1));
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request, libraries);
    if (request.operation === "libraries.get") return libraryDetail(request);
    throw new Error(`unexpected operation ${request.operation}`);
  });
  await page.goto("/libraries");
  await expect(page.getByRole("list", { name: "资源库列表" })).toBeVisible();
  const rows = page.locator(".library-table-row");
  await expect.poll(() => rows.count()).toBeGreaterThan(0);
  const windowed = await rows.count();
  expect(windowed).toBeLessThan(libraries.length);
  await page.waitForTimeout(1200);
  expect(await rows.count()).toBe(windowed);
  expect(loops).toEqual([]);
});

test("the asset window follows a scroll, a resize, a view change and the keyboard", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const loops = watchForRecursiveUpdates(page);
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request);
    if (request.operation === "entries.get") return entryDetail(request, entry(1));
    if (request.operation === "entries.browse") return browsePage(request, assetPage());
    throw new Error(`unexpected operation ${request.operation}`);
  });
  await page.goto(browsePath);
  const list = page.getByRole("listbox", { name: "资产列表" });
  await expect(list).toBeVisible();
  const rows = page.locator("[data-entry-row]");
  await expect.poll(() => rows.count()).toBeGreaterThan(0);

  // Scrolling really moves the window: the first row on screen is no longer the first row of the list.
  const first = page.locator("[data-entry-id]").first();
  const before = await first.getAttribute("data-entry-id");
  await list.evaluate((element) => {
    element.scrollTop = 1200;
  });
  await expect
    .poll(async () => page.locator("[data-entry-id]").first().getAttribute("data-entry-id"))
    .not.toBe(before);
  expect(await rows.count()).toBeLessThan(assetPage().length);

  // A narrower container re-measures: the same list still renders a bounded window of rows.
  await page.setViewportSize({ width: 1024, height: 700 });
  await expect.poll(() => rows.count()).toBeGreaterThan(0);
  expect(await rows.count()).toBeLessThan(assetPage().length);

  // The grid view changes the row height, so the window has to be computed again rather than reused.
  await page.getByRole("button", { name: "网格视图" }).click();
  await expect(page).toHaveURL(/view=grid/);
  await expect.poll(() => rows.count()).toBeGreaterThan(0);
  expect(await rows.count()).toBeLessThan(assetPage().length);

  // A keyboard move scrolls the focused row back into view and leaves it focused.
  await page.getByRole("button", { name: "列表视图" }).click();
  await page.locator("[data-entry-id]").first().focus();
  await page.keyboard.press("End");
  await expect.poll(async () => list.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
  await expect(page.locator("[data-entry-id]:focus")).toHaveCount(1);
  expect(loops).toEqual([]);
});
