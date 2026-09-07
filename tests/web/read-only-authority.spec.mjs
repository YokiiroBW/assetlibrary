import { expect, test } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import {
  browsePage,
  entry,
  failure,
  libraryPage,
  mockAssetLink,
  searchPage,
  visibleLibrary,
} from "./assetlink-fixtures.mjs";

test("reconnecting after authentication loss discards the previous identity's selection", async ({ page }) => {
  let identity = "first";
  const privateEntry = entry(901, { name: "previous-identity.png", relative_path: "previous-identity.png" });
  await mockAssetLink(page, async (request) => {
    if (identity === "second") {
      return request.operation === "libraries.list"
        ? libraryPage(request, [])
        : failure(request, 404, "not_found", "The requested resource is not available.");
    }
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "entries.browse" && request.body.cursor === undefined) {
      return browsePage(request, [privateEntry], "expires-next");
    }
    return failure(request, 401, "authentication_required", "Authentication is required.");
  });

  await page.goto("/");
  await page.getByRole("button", { name: "previous-identity.png previous-identity.png" }).click();
  await expect(page.locator(".detail-pane").getByRole("heading", { name: privateEntry.name })).toBeVisible();
  await page.getByRole("button", { name: "载入更多", exact: true }).click();
  await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();

  identity = "second";
  await page.getByRole("button", { name: "重试连接" }).click();
  await expect(page.getByRole("heading", { name: "没有可见资源库" })).toBeVisible();
  await expect(page.locator(".detail-pane")).not.toContainText(privateEntry.name);
  await expect(page.locator("[data-entry-row]")).toHaveCount(0);
});

for (const status of [403, 404]) {
  test(`a ${status} response while paging removes cached entries and details`, async ({ page }) => {
    const previousEntry = entry(902);
    await mockAssetLink(page, async (request) => {
      if (request.operation === "libraries.list") return libraryPage(request);
      return request.body.cursor === undefined
        ? browsePage(request, [previousEntry], "next-page")
        : failure(request, status, "not_found", "The requested resource is not available.");
    });

    await page.goto("/");
    await page.getByRole("button", { name: `${previousEntry.name} ${previousEntry.relative_path}` }).click();
    await page.getByRole("button", { name: "载入更多", exact: true }).click();
    await expect(page.getByRole("heading", { name: "读取失败" })).toBeVisible();
    await expect(page.locator("[data-entry-row]")).toHaveCount(0);
    await expect(page.locator(".detail-pane")).not.toContainText(previousEntry.name);
    await expect(page.getByRole("button", { name: "载入更多", exact: true })).toHaveCount(0);
  });
}

test("a rejected library page clears the dependent browse and selected details", async ({ page }) => {
  const previousEntry = entry(904);
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") {
      return request.body.cursor === undefined
        ? libraryPage(request, [visibleLibrary], "more-libraries")
        : failure(request, 403, "forbidden", "The requested resource is not available.");
    }
    return browsePage(request, [previousEntry]);
  });

  await page.goto("/");
  await page.getByRole("button", { name: `${previousEntry.name} ${previousEntry.relative_path}` }).click();
  await page.getByRole("button", { name: "载入更多资源库" }).click();
  await expect(page.getByRole("heading", { name: "读取失败" })).toBeVisible();
  await expect(page.locator("[data-library-id]")).toHaveCount(0);
  await expect(page.locator("[data-entry-row]")).toHaveCount(0);
  await expect(page.locator(".detail-pane")).not.toContainText(previousEntry.name);
});

test("a rejected search page removes earlier matches and the selected match", async ({ page }) => {
  const previousEntry = entry(905);
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "entries.browse") return browsePage(request);
    return request.body.cursor === undefined
      ? searchPage(request, [{ library: visibleLibrary, entry: previousEntry, hit_reason: "name" }], "more-hits")
      : failure(request, 403, "forbidden", "The requested resource is not available.");
  });

  await page.goto("/");
  await page.getByRole("searchbox").fill("asset");
  await page.getByRole("button", { name: `${previousEntry.name} ${visibleLibrary.display_name} · 名称命中` }).click();
  await page.getByRole("button", { name: "载入更多", exact: true }).click();
  await expect(page.getByRole("heading", { name: "读取失败" })).toBeVisible();
  await expect(page.locator("[data-entry-row]")).toHaveCount(0);
  await expect(page.locator(".detail-pane")).not.toContainText(previousEntry.name);
});

for (const [status, format] of [
  [401, "HTML"],
  [401, "malformed error"],
  [403, "malformed error"],
  [404, "malformed error"],
]) {
  test(`a ${status} rejection with a ${format} body still hides cached data`, async ({ page }) => {
    const previousEntry = entry(906);
    await page.route("**/assetlink/v1/control", async (route) => {
      const request = route.request().postDataJSON();
      if (request.body.cursor !== undefined) {
        await route.fulfill({
          status,
          contentType: format === "HTML" ? "text/html" : "application/json",
          body:
            format === "HTML"
              ? "<p>Authentication required</p>"
              : JSON.stringify({
                  message_type: "error",
                  request_id: request.request_id,
                  error: { code: 1, message: null, retryable: false },
                }),
        });
        return;
      }
      const response =
        request.operation === "libraries.list"
          ? libraryPage(request)
          : browsePage(request, [previousEntry], "expired-page");
      await route.fulfill({ contentType: "application/json", body: JSON.stringify(response.body) });
    });

    await page.goto("/");
    await page.getByRole("button", { name: `${previousEntry.name} ${previousEntry.relative_path}` }).click();
    await page.getByRole("button", { name: "载入更多", exact: true }).click();
    await expect(page.getByRole("heading", { name: status === 401 ? "登录状态已失效" : "读取失败" })).toBeVisible();
    await expect(page.getByText(previousEntry.name, { exact: true })).toHaveCount(0);
  });
}

test("a transient paging error preserves the last readable page while showing the error", async ({ page }) => {
  const previousEntry = entry(903);
  await mockAssetLink(page, async (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    return request.body.cursor === undefined
      ? browsePage(request, [previousEntry], "next-page")
      : failure(request, 503, "service_unavailable", "只读服务暂时不可用。");
  });

  await page.goto("/");
  await page.getByRole("button", { name: "载入更多", exact: true }).click();
  await expect(page.getByRole("heading", { name: "读取失败" })).toBeVisible();
  await expect(
    page.getByRole("button", { name: `${previousEntry.name} ${previousEntry.relative_path}` }),
  ).toBeVisible();
  await expect(page.getByRole("heading", { name: "这个目录是空的" })).toHaveCount(0);
});
