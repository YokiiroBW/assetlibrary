import { expect, test } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import {
  browsePage,
  entry,
  failure,
  hiddenLibraryName,
  libraryPage,
  mockAssetLink,
  searchPage,
  visibleLibrary,
} from "./assetlink-fixtures.mjs";

test("desktop browse stays permission-filtered, paged, and virtualized", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const browseCursors = [];
  const folder = entry(1, {
    entry_id: "33333333-3333-4333-8333-333333333333",
    relative_path: "Folder",
    name: "Folder",
    kind: "directory",
    content_length: null,
  });
  const rootItems = [folder, ...Array.from({ length: 99 }, (_, index) => entry(index + 2))];
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "entries.browse") {
      browseCursors.push(request.body.cursor ?? null);
      if (request.body.parent_relative_path === "Folder") {
        return browsePage(request, [entry(501, { relative_path: "Folder/inside.jpg", name: "inside.jpg" })]);
      }
      return request.body.cursor === "root-next"
        ? browsePage(request, [entry(500)], null)
        : browsePage(request, rootItems, "root-next");
    }
    throw new Error(`unexpected operation ${request.operation}`);
  });

  await page.goto("/");
  await expect(page.getByRole("heading", { name: "根目录" })).toBeVisible();
  await expect(page.getByRole("button", { name: "设计素材 只读 在线" })).toBeVisible();
  await expect(page.getByText(hiddenLibraryName, { exact: true })).toHaveCount(0);
  await expect(page.locator("[data-entry-row]")).not.toHaveCount(100);
  expect(await page.locator("[data-entry-row]").count()).toBeLessThan(40);

  await page.getByRole("button", { name: "载入更多" }).click();
  await expect.poll(() => browseCursors.includes("root-next")).toBeTruthy();

  await page.getByRole("button", { name: "打开目录 Folder" }).click();
  await expect(page.getByRole("heading", { name: "Folder" })).toBeVisible();
  await expect(page.getByText("inside.jpg", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "inside.jpg Folder/inside.jpg" }).click();
  await expect(page.locator(".detail-pane").getByRole("heading", { name: "inside.jpg" })).toBeVisible();
  await expect(page.locator(".detail-pane")).toContainText("Folder/inside.jpg");

  await page.getByRole("button", { name: "返回上级" }).click();
  await expect(page.getByRole("heading", { name: "根目录" })).toBeVisible();
});

test("narrow workspace reflows and presents the empty directory state", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "entries.browse") return browsePage(request);
    throw new Error(`unexpected operation ${request.operation}`);
  });

  await page.goto("/");
  await expect(page.getByRole("heading", { name: "这个目录是空的" })).toBeVisible();
  const columns = await page.locator(".workspace").evaluate((element) => getComputedStyle(element).gridTemplateColumns);
  expect(columns.trim().split(/\s+/)).toHaveLength(1);
});

test("read errors and expired authentication have distinct fail-closed states", async ({ page }) => {
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    return failure(request, 503, "service_unavailable", "只读服务暂时不可用。");
  });
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "读取失败" })).toBeVisible();
  await expect(page.getByText("只读服务暂时不可用。")).toBeVisible();

  await page.unrouteAll({ behavior: "wait" });
  await mockAssetLink(page, async (request) =>
    failure(request, 401, "authentication_required", "Authentication is required."),
  );
  await page.reload();
  await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();
  await expect(page.getByText("请先通过宿主身份系统重新登录，然后再重试。")).toBeVisible();

  await page.unrouteAll({ behavior: "wait" });
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "entries.browse") return browsePage(request);
    return failure(request, 401, "authentication_required", "Authentication is required.");
  });
  await page.reload();
  await page.getByRole("searchbox", { name: "搜索文件名或相对路径" }).fill("expired");
  await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();

  await page.unrouteAll({ behavior: "wait" });
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "entries.browse") return browsePage(request);
    if (request.operation === "assets.search") return searchPage(request);
    throw new Error(`unexpected operation ${request.operation}`);
  });
  await page.getByRole("button", { name: "重试连接" }).click();
  await expect(page.getByRole("heading", { name: "没有匹配结果" })).toBeVisible();
});

test("a search result can open a library not yet loaded in the sidebar", async ({ page }) => {
  const archiveLibrary = {
    ...visibleLibrary,
    library_id: "44444444-4444-4444-8444-444444444444",
    display_name: "归档素材",
  };
  const archiveFolder = entry(800, {
    entry_id: "55555555-5555-4555-8555-555555555555",
    library_id: archiveLibrary.library_id,
    relative_path: "Archive",
    name: "Archive",
    kind: "directory",
    content_length: null,
  });
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "assets.search") {
      return searchPage(request, [{ library: archiveLibrary, entry: archiveFolder, hit_reason: "name" }]);
    }
    if (request.operation === "entries.browse") {
      return browsePage(
        request,
        [
          entry(801, {
            library_id: archiveLibrary.library_id,
            relative_path: "Archive/inside.jpg",
            name: "inside.jpg",
          }),
        ],
        null,
        archiveLibrary,
      );
    }
    throw new Error(`unexpected operation ${request.operation}`);
  });

  await page.goto("/");
  await page.getByRole("searchbox", { name: "搜索文件名或相对路径" }).fill("Archive");
  await page.getByRole("button", { name: "打开目录 Archive" }).click();
  await expect(page.getByRole("heading", { name: "Archive" })).toBeVisible();
  await expect(page.getByText("inside.jpg", { exact: true })).toBeVisible();
  await expect(page.getByText("归档素材", { exact: true })).toBeVisible();
});

test("mismatched and oversized response pages fail closed", async ({ page }) => {
  let invalidMode = "request-id";
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "entries.browse") {
      const response = browsePage(
        request,
        invalidMode === "page-size" ? Array.from({ length: 101 }, (_, index) => entry(index)) : [],
      );
      if (invalidMode === "request-id") response.body.request_id = "different-request";
      return response;
    }
    throw new Error(`unexpected operation ${request.operation}`);
  });

  await page.goto("/");
  await expect(page.getByRole("heading", { name: "读取失败" })).toBeVisible();

  invalidMode = "page-size";
  await page.reload();
  await expect(page.getByRole("heading", { name: "读取失败" })).toBeVisible();
});

test("a stale search response cannot replace the newest query", async ({ page }) => {
  const seenQueries = [];
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "entries.browse") return browsePage(request);
    if (request.operation === "assets.search") {
      const query = request.body.query;
      seenQueries.push(query);
      if (query === "old") await new Promise((resolve) => setTimeout(resolve, 600));
      const matched = entry(query === "old" ? 701 : 702, {
        relative_path: `${query}-result.jpg`,
        name: `${query}-result.jpg`,
      });
      return searchPage(request, [{ library: visibleLibrary, entry: matched, hit_reason: "name" }]);
    }
    throw new Error(`unexpected operation ${request.operation}`);
  });

  await page.goto("/");
  const search = page.getByRole("searchbox", { name: "搜索文件名或相对路径" });
  await search.fill("old");
  await expect.poll(() => seenQueries).toContain("old");
  await search.fill("new");
  await expect(page.getByText("new-result.jpg", { exact: true })).toBeVisible();
  await page.waitForTimeout(700);
  await expect(page.getByText("old-result.jpg", { exact: true })).toHaveCount(0);
  expect(seenQueries).toContain("new");
});
