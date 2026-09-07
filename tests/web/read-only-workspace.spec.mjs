import { expect, test } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import {
  browsePage,
  browsePath,
  entry,
  entryDetail,
  entryOption,
  failure,
  hiddenLibraryName,
  libraryDetail,
  libraryPage,
  mockAssetLink,
  mockSession,
  searchPage,
  visibleLibrary,
} from "./assetlink-fixtures.mjs";
import { mockTrial } from "./trial-fixtures.mjs";

test.beforeEach(async ({ page }) => {
  await mockSession(page);
});

test("desktop browse stays permission-filtered, paged, virtualized and restores navigation", async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const browseCursors = [];
  const folder = entry(1, { relative_path: "Folder", name: "Folder", kind: "directory", content_length: null });
  const inside = entry(501, { relative_path: "Folder/inside.jpg", name: "inside.jpg" });
  const rootItems = [folder, ...Array.from({ length: 99 }, (_, index) => entry(index + 2))];
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request);
    if (request.operation === "entries.get")
      return entryDetail(request, request.body.entry_id === inside.entry_id ? inside : folder);
    if (request.operation === "entries.browse") {
      browseCursors.push(request.body.cursor ?? null);
      if (request.body.parent_relative_path === "Folder") return browsePage(request, [inside]);
      return request.body.cursor === "root-next"
        ? browsePage(request, [entry(500)])
        : browsePage(request, rootItems, "root-next");
    }
    throw new Error(`unexpected operation ${request.operation}`);
  });
  await page.goto(browsePath);
  await expect(page.getByRole("heading", { name: "根目录" })).toBeVisible();
  await expect(
    page.getByRole("navigation", { name: "已加载资源库" }).getByRole("link", { name: /设计素材/ }),
  ).toBeVisible();
  await expect(page.getByText(hiddenLibraryName, { exact: true })).toHaveCount(0);
  expect(await page.locator("[data-entry-row]").count()).toBeLessThan(40);
  await page.getByRole("button", { name: "载入更多", exact: true }).click();
  await expect.poll(() => browseCursors.includes("root-next")).toBeTruthy();
  await entryOption(page, "Folder").dblclick();
  await expect(page).toHaveURL(/path=Folder/);
  await expect(entryOption(page, "inside.jpg")).toBeVisible();
  await entryOption(page, "inside.jpg").dblclick();
  await expect(page).toHaveURL(new RegExp(`entry=${inside.entry_id}`));
  await expect(page.locator(".detail-pane").getByRole("heading", { name: "inside.jpg" })).toBeVisible();
  await page.reload();
  await expect(page.locator(".detail-pane")).toContainText("Folder/inside.jpg");
  await page.goBack();
  await expect(page).not.toHaveURL(/entry=/);
  await page.goBack();
  await expect(page.getByRole("heading", { name: "根目录" })).toBeVisible();
  await page.goForward();
  await expect(entryOption(page, "inside.jpg")).toBeVisible();
  await page.getByRole("link", { name: "返回上级" }).click();
  await expect(entryOption(page, "Folder")).toBeVisible();
  await expect(page.locator(".detail-pane")).not.toContainText("inside.jpg");
  await page.screenshot({ path: testInfo.outputPath("workspace-desktop.png"), animations: "disabled" });
});

test("narrow workspace reflows and presents the empty directory state", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockAssetLink(page, (request) =>
    request.operation === "libraries.list"
      ? libraryPage(request)
      : request.operation === "libraries.get"
        ? libraryDetail(request)
        : browsePage(request),
  );
  await page.goto(browsePath);
  await expect(page.getByRole("heading", { name: "这个范围没有条目" })).toBeVisible();
  expect(
    (await page.locator(".workspace").evaluate((element) => getComputedStyle(element).gridTemplateColumns))
      .trim()
      .split(/\s+/),
  ).toHaveLength(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.getByRole("button", { name: "打开工作区导航" }).click();
  const navigation = page.getByRole("dialog", { name: "工作区导航" });
  await expect(navigation.getByRole("link", { name: "首页", exact: true })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(navigation).toHaveCount(0);
  await expect(page.getByRole("button", { name: "打开工作区导航" })).toBeFocused();
  await page.screenshot({ path: testInfo.outputPath("workspace-narrow.png"), animations: "disabled" });
});

test("directory responses refresh library availability without reloading the catalog", async ({ page }) => {
  let libraryReads = 0;
  const folder = entry(1, { name: "Folder", relative_path: "Folder", kind: "directory", content_length: null });
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") {
      libraryReads++;
      return libraryPage(request);
    }
    if (request.operation === "libraries.get") return libraryDetail(request);
    return request.body.parent_relative_path === "Folder"
      ? browsePage(request, [], null, { ...visibleLibrary, availability: "offline" })
      : browsePage(request, [folder]);
  });
  await page.goto(browsePath);
  await expect(entryOption(page, "Folder")).toBeVisible();
  const initialReads = libraryReads;
  await entryOption(page, "Folder").dblclick();
  await expect(page.getByText(/此资源库暂时离线/)).toBeVisible();
  await expect(page.locator(".library-list [aria-label='离线']")).toBeVisible();
  await expect(page.getByRole("heading", { name: "这个范围没有条目" })).toHaveCount(0);
  expect(libraryReads).toBe(initialReads);
});

test("read errors and expired authentication have distinct fail-closed states", async ({ page }) => {
  let expired = false;
  let unavailable = true;
  await mockAssetLink(page, (request) => {
    if (expired) return failure(request, 401, "authentication_required", "Authentication is required.");
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request);
    return unavailable ? failure(request, 503, "service_unavailable", "只读服务暂时不可用。") : browsePage(request);
  });
  await page.goto(browsePath);
  await expect(page.getByRole("heading", { name: "暂时无法读取" })).toBeVisible();
  await expect(page.getByText("只读服务暂时不可用。")).toBeVisible();
  expired = true;
  await page.getByRole("button", { name: "重试", exact: true }).click();
  await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();
  expired = false;
  unavailable = false;
  await page.getByRole("button", { name: "重试连接" }).click();
  await expect(page.getByRole("heading", { name: "首页", exact: true })).toBeVisible();
  await expect(page.getByRole("searchbox")).toHaveValue("");
});

test("search opens an unloaded library and locates a file beyond the first page", async ({ page }) => {
  const archiveLibrary = {
    ...visibleLibrary,
    library_id: "44444444-4444-4444-8444-444444444444",
    display_name: "归档素材",
  };
  const archiveFile = entry(800, {
    library_id: archiveLibrary.library_id,
    relative_path: "Archive/late.jpg",
    name: "late.jpg",
  });
  const browseRequests = [];
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request, archiveLibrary);
    if (request.operation === "entries.get") return entryDetail(request, archiveFile, archiveLibrary);
    if (request.operation === "assets.search")
      return searchPage(request, [{ library: archiveLibrary, entry: archiveFile, hit_reason: "name" }]);
    if (request.operation === "entries.browse") {
      browseRequests.push(request.body);
      if (request.body.anchor_entry_id === archiveFile.entry_id)
        return browsePage(request, [archiveFile], "after-anchor", archiveLibrary);
      return browsePage(request, [], null, archiveLibrary);
    }
    throw new Error(`unexpected operation ${request.operation}`);
  });
  await page.goto("/");
  await page.getByRole("searchbox").fill("late");
  await entryOption(page, "late.jpg").dblclick();
  await expect(page).toHaveURL(/entry_library=44444444/);
  await page.locator(".detail-pane").getByRole("button", { name: "定位所在目录" }).click();
  await expect(page).toHaveURL(new RegExp(`path=Archive&anchor=${archiveFile.entry_id}`));
  await expect(entryOption(page, "late.jpg")).toBeVisible();
  expect(browseRequests[0]).toMatchObject({
    library_id: archiveLibrary.library_id,
    parent_relative_path: "Archive",
    anchor_entry_id: archiveFile.entry_id,
  });
  expect(browseRequests[0]).not.toHaveProperty("cursor");
  await page.getByRole("button", { name: "载入更多", exact: true }).click();
  await expect.poll(() => browseRequests.length).toBe(2);
  expect(browseRequests[1].cursor).toBe("after-anchor");
  expect(browseRequests[1]).not.toHaveProperty("anchor_entry_id");
});

test("mismatched and oversized response pages fail closed", async ({ page }) => {
  let invalidMode = "request-id";
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request);
    const response = browsePage(
      request,
      invalidMode === "page-size" ? Array.from({ length: 101 }, (_, index) => entry(index)) : [],
    );
    if (invalidMode === "request-id") response.body.request_id = "different-request";
    return response;
  });
  await page.goto(browsePath);
  await expect(page.getByRole("heading", { name: "暂时无法读取" })).toBeVisible();
  invalidMode = "page-size";
  await page.reload();
  await expect(page.getByRole("heading", { name: "暂时无法读取" })).toBeVisible();
});

test("a stale search response cannot replace the newest query", async ({ page }) => {
  const seenQueries = [];
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    const query = request.body.query;
    seenQueries.push(query);
    if (query === "old") await new Promise((resolve) => setTimeout(resolve, 600));
    const matched = entry(query === "old" ? 701 : 702, {
      relative_path: `${query}-result.jpg`,
      name: `${query}-result.jpg`,
    });
    return searchPage(request, [{ library: visibleLibrary, entry: matched, hit_reason: "name" }]);
  });
  await page.goto("/");
  const search = page.getByRole("searchbox");
  await search.fill("old");
  await expect.poll(() => seenQueries).toContain("old");
  await search.fill("new");
  await expect(entryOption(page, "new-result.jpg")).toBeVisible();
  await page.waitForTimeout(700);
  await expect(page.getByText("old-result.jpg", { exact: true })).toHaveCount(0);
});

test("home, category management and conflicts use explicit server metadata", async ({ page }, testInfo) => {
  const state = await mockTrial(page);
  state.library = { ...state.library, display_name: "名字含照片但分类为图片", category: "images" };
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "首页", exact: true })).toBeVisible();
  expect(state.requests.some((request) => request.operation.startsWith("library_scans."))).toBeFalsy();
  await page.getByRole("navigation", { name: "分类入口" }).getByRole("link", { name: "照片", exact: true }).click();
  await expect(page).toHaveURL(/\/categories\/photos$/);
  await expect(page.getByRole("heading", { name: "此分类暂无资源库" })).toBeVisible();
  expect(state.requests.filter((request) => request.operation === "libraries.list").at(-1).body.category).toBe(
    "photos",
  );
  await page.getByRole("navigation", { name: "资源分类" }).getByRole("link", { name: "图片", exact: true }).click();
  await page.getByRole("button", { name: "修改分类", exact: true }).click();
  let dialog = page.getByRole("dialog", { name: "修改资源库分类" });
  await dialog.getByLabel("资源库分类", { exact: true }).selectOption("photos");
  state.categoryFailure = 409;
  await dialog.getByRole("button", { name: "保存分类" }).click();
  await expect(dialog.getByRole("alert")).toBeFocused();
  await expect(dialog.getByRole("button", { name: "重新读取" })).toBeVisible();
  state.categoryFailure = null;
  await dialog.getByRole("button", { name: "重新读取" }).click();
  await page.getByRole("button", { name: "修改分类", exact: true }).click();
  dialog = page.getByRole("dialog", { name: "修改资源库分类" });
  await dialog.getByLabel("资源库分类", { exact: true }).selectOption("photos");
  await dialog.getByRole("button", { name: "保存分类" }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "此分类暂无资源库" })).toBeVisible();
  const update = state.requests.filter((request) => request.operation === "libraries.update_category").at(-1);
  expect(update.body).toEqual({
    library_id: state.library.library_id,
    category: "photos",
    expected_category: "images",
  });
  expect(update.idempotency_key).toMatch(/^[0-9a-f-]{36}$/);
  await page.getByRole("navigation", { name: "工作区导航" }).getByRole("link", { name: "首页", exact: true }).click();
  await page.screenshot({ path: testInfo.outputPath("workspace-home.png"), animations: "disabled" });
});

test("multi-selection, keyboard, grid and mobile details share real entry information", async ({ page }, testInfo) => {
  const items = [entry(10), entry(11), entry(12)];
  await page.addInitScript(() => {
    Object.defineProperty(navigator.clipboard, "writeText", {
      value: async (text) => {
        window.copiedRelativePaths = text;
      },
    });
  });
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request);
    if (request.operation === "assets.search")
      return searchPage(request, [{ library: visibleLibrary, entry: items[0], hit_reason: "path" }]);
    if (request.operation === "entries.get")
      return entryDetail(
        request,
        items.find((item) => item.entry_id === request.body.entry_id),
      );
    return browsePage(request, items);
  });
  await page.goto(browsePath);
  await entryOption(page, items[0].name).click();
  await entryOption(page, items[2].name).click({ modifiers: ["Shift"] });
  await expect(page.getByRole("listbox", { name: "资产列表" }).getByRole("option", { selected: true })).toHaveCount(3);
  await page.locator(".collection-toolbar").getByRole("button", { name: "复制相对路径" }).click();
  expect(await page.evaluate(() => window.copiedRelativePaths)).toBe(
    items.map((item) => item.relative_path).join("\n"),
  );
  await page.getByRole("button", { name: "网格视图" }).click();
  await expect(page).toHaveURL(/view=grid/);
  await expect(page.getByRole("listbox", { name: "资产列表" }).getByRole("option", { selected: true })).toHaveCount(3);
  await entryOption(page, items[0].name).click();
  await page.keyboard.press("ArrowDown");
  await expect(page.getByRole("listbox", { name: "资产列表" }).getByRole("option", { selected: true })).toHaveCount(1);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("listbox", { name: "资产列表" }).getByRole("option", { selected: true })).toHaveCount(0);
  await page.keyboard.press("Home");
  await page.keyboard.press("Enter");
  await expect(page.locator(".detail-pane").getByRole("heading", { name: items[0].name })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("workspace-grid-details.png"), animations: "disabled" });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ colorScheme: "dark", reducedMotion: "reduce" });
  const drawer = page.getByRole("dialog", { name: "资产详情" });
  await expect(drawer.getByRole("heading", { name: items[0].name })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.screenshot({ path: testInfo.outputPath("workspace-mobile-details-dark.png"), animations: "disabled" });
  await page.keyboard.press("Escape");
  await expect(drawer).toHaveCount(0);
  await expect(page).not.toHaveURL(/entry=/);
  await expect(page.locator(".detail-pane")).toHaveCount(0);
  await page.getByRole("searchbox").fill(items[0].name);
  await page.getByRole("button", { name: "列表视图" }).click();
  const resultEntry = entryOption(page, items[0].name);
  await expect(resultEntry).toBeVisible();
  const unselectedPosition = await resultEntry.boundingBox();
  await resultEntry.click();
  await expect(resultEntry).toHaveAttribute("aria-selected", "true");
  expect((await resultEntry.boundingBox()).y).toBe(unselectedPosition.y);
  await page.getByRole("button", { name: "取消选择", exact: true }).click();
  await expect(resultEntry).toHaveAttribute("aria-selected", "false");
  expect((await resultEntry.boundingBox()).y).toBe(unselectedPosition.y);
  await resultEntry.dblclick();
  await expect(drawer.getByRole("heading", { name: items[0].name })).toBeVisible();
});

test("scope, server-wide filters and bounded deep links survive refresh and history", async ({ page }) => {
  const queries = [];
  const browses = [];
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request);
    if (request.operation === "assets.search") {
      queries.push(request.body);
      return searchPage(request);
    }
    browses.push(request.body);
    return browsePage(request);
  });
  await page.goto(`${browsePath}?path=Album%3A2026`);
  await page.getByLabel("排序依据", { exact: true }).selectOption("size");
  await page.getByLabel("排序方向", { exact: true }).selectOption("desc");
  await page.getByLabel("条目类型", { exact: true }).selectOption("files");
  await page.getByLabel("筛选当前目录名称", { exact: true }).fill("%_literal");
  await page.getByRole("button", { name: "筛选", exact: true }).click();
  await expect.poll(() => browses.at(-1)?.name_filter).toBe("%_literal");
  expect(browses.at(-1)).toMatchObject({
    sort_by: "size",
    sort_direction: "desc",
    kind: "files",
    parent_relative_path: "Album:2026",
  });
  await page.getByRole("searchbox").fill("asset");
  await expect.poll(() => queries.at(-1)?.scope).toBe("library");
  await page.getByLabel("搜索范围", { exact: true }).selectOption("all");
  await expect.poll(() => queries.at(-1)?.scope).toBe("all");
  expect(queries.at(-1)).not.toHaveProperty("library_id");
  expect(queries.at(-1)).not.toHaveProperty("parent_relative_path");
  await expect(page).toHaveURL(/from_library=.*from_path=Album%3A2026/);
  await page.reload();
  await page.getByLabel("搜索范围", { exact: true }).selectOption("directory");
  await expect.poll(() => queries.at(-1)?.scope).toBe("directory");
  expect(queries.at(-1)).toMatchObject({ library_id: visibleLibrary.library_id, parent_relative_path: "Album:2026" });
  await page.goBack();
  await expect(page.getByLabel("搜索范围", { exact: true })).toHaveValue("all");
  for (const path of [
    "/libraries/not-an-id",
    "/search?scope=unknown&q=asset",
    "/search?scope=all&path=Album",
    `${browsePath}?path=C%3A%2Fsecret`,
    `${browsePath}?name=${"x".repeat(201)}`,
  ]) {
    const before = browses.length + queries.length;
    await page.goto(path);
    await expect(page.getByRole("heading", { name: "无法打开此地址" })).toBeVisible();
    expect(browses.length + queries.length).toBe(before);
  }
});
