import { expect, test } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import {
  browsePage,
  entry,
  failure,
  libraryPage,
  mockSession,
  searchPage,
  visibleLibrary,
} from "./assetlink-fixtures.mjs";
import { sendResult, startAssetLinkServer } from "./hanging-assetlink-server.mjs";

test.beforeEach(async ({ page }) => {
  await mockSession(page);
});

for (const pendingStage of ["headers", "body"]) {
  test(`a request with stalled ${pendingStage} times out, closes the connection, and can retry`, async ({ page }) => {
    let shouldStall = true;
    const closedAfter = [];
    const advertisedDeadlines = [];
    const stop = await startAssetLinkServer(page, (request, response) => {
      advertisedDeadlines.push(request.timeout_ms);
      if (request.operation === "libraries.list") {
        sendResult(response, libraryPage(request));
      } else if (shouldStall) {
        const started = Date.now();
        response.on("close", () => closedAfter.push(Date.now() - started));
        if (pendingStage === "body") {
          response.write(`{"message_type":"control.result","request_id":"${request.request_id}","ok":true,"body":`);
          response.flushHeaders();
        }
      } else {
        sendResult(response, browsePage(request, [entry(950)]));
      }
    });
    try {
      await page.goto("/");
      await expect(page.getByRole("heading", { name: "读取失败" })).toBeVisible({ timeout: 8_000 });
      await expect(page.getByText("读取超时，请重试。")).toBeVisible();
      await expect.poll(() => closedAfter.length).toBe(1);
      expect(closedAfter[0]).toBeGreaterThanOrEqual(4_000);
      expect(closedAfter[0]).toBeLessThan(8_000);
      expect(advertisedDeadlines.every((value) => value === 5_000)).toBeTruthy();

      shouldStall = false;
      await page.getByRole("button", { name: "重试", exact: true }).click();
      await expect(page.getByRole("button", { name: "asset-0950.png asset-0950.png" })).toBeVisible();
      await expect(page.getByRole("heading", { name: "读取失败" })).toHaveCount(0);
    } finally {
      await stop();
    }
  });
}

test("changing the query cancels its pending request without reporting a timeout", async ({ page }) => {
  const oldRequestsClosedAfter = [];
  let oldRequestStarted = false;
  const stop = await startAssetLinkServer(page, (request, response) => {
    if (request.operation === "libraries.list") {
      sendResult(response, libraryPage(request));
    } else if (request.operation === "entries.browse") {
      sendResult(response, browsePage(request));
    } else if (request.body.query === "old") {
      oldRequestStarted = true;
      const started = Date.now();
      response.on("close", () => oldRequestsClosedAfter.push(Date.now() - started));
    } else {
      const hit = entry(951, { name: "new-result.jpg", relative_path: "new-result.jpg" });
      sendResult(response, searchPage(request, [{ library: visibleLibrary, entry: hit, hit_reason: "name" }]));
    }
  });
  try {
    await page.goto("/");
    const search = page.getByRole("searchbox");
    await search.fill("old");
    await expect.poll(() => oldRequestStarted).toBeTruthy();
    await search.fill("new");
    await expect(page.getByText("new-result.jpg", { exact: true })).toBeVisible();
    await expect.poll(() => oldRequestsClosedAfter.length).toBe(1);
    expect(oldRequestsClosedAfter[0]).toBeLessThan(2_000);
    await page.waitForTimeout(5_100);
    await expect(page.getByText("new-result.jpg", { exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { name: "读取失败" })).toHaveCount(0);
  } finally {
    await stop();
  }
});

test("reconnecting cancels a pending later page from the previous workspace", async ({ page }) => {
  let reconnected = false;
  let laterPageStarted = false;
  const laterPageClosedAfter = [];
  const stop = await startAssetLinkServer(page, (request, response) => {
    if (request.operation === "libraries.list") {
      sendResult(response, libraryPage(request, reconnected ? [] : [visibleLibrary]));
    } else if (request.operation === "assets.search") {
      sendResult(response, failure(request, 401, "authentication_required", "Authentication is required."));
    } else if (request.body.cursor === undefined) {
      sendResult(response, browsePage(request, [entry(952)], "pending-page"));
    } else {
      laterPageStarted = true;
      const started = Date.now();
      response.on("close", () => laterPageClosedAfter.push(Date.now() - started));
    }
  });
  try {
    await page.goto("/");
    await page.getByRole("button", { name: "载入更多", exact: true }).click();
    await expect.poll(() => laterPageStarted).toBeTruthy();
    await page.getByRole("searchbox").fill("expired");
    await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();
    reconnected = true;
    await page.getByRole("button", { name: "重试连接" }).click();
    await expect(page.getByRole("heading", { name: "没有可见资源库" })).toBeVisible();
    await expect.poll(() => laterPageClosedAfter.length).toBe(1);
    expect(laterPageClosedAfter[0]).toBeLessThan(2_000);
  } finally {
    await stop();
  }
});

test("a pre-cancelled caller is rejected before sending an AssetLink request", async ({ page }) => {
  let requests = 0;
  const stop = await startAssetLinkServer(page, (request, response) => {
    requests++;
    sendResult(response, request.operation === "libraries.list" ? libraryPage(request, []) : browsePage(request));
  });
  try {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "没有可见资源库" })).toBeVisible();
    const before = requests;
    const cancelled = await page.evaluate(async () => {
      const { AssetLinkClient } = await import("/src/assetLinkClient.ts");
      const controller = new AbortController();
      const reason = new DOMException("Caller cancelled", "AbortError");
      controller.abort(reason);
      try {
        await new AssetLinkClient().listLibraries(null, controller.signal);
        return false;
      } catch (error) {
        return error === reason;
      }
    });
    expect(cancelled).toBeTruthy();
    expect(requests).toBe(before);
  } finally {
    await stop();
  }
});

test("an authentication rejection keeps its status when its response body stalls", async ({ page }) => {
  const previousEntry = entry(953);
  const closedAfter = [];
  const stop = await startAssetLinkServer(page, (request, response) => {
    if (request.operation === "libraries.list") {
      sendResult(response, libraryPage(request));
    } else if (request.body.cursor === undefined) {
      sendResult(response, browsePage(request, [previousEntry], "expired-page"));
    } else {
      const started = Date.now();
      response.on("close", () => closedAfter.push(Date.now() - started));
      response.statusCode = 401;
      response.write(`{"message_type":"error","request_id":"${request.request_id}","error":`);
      response.flushHeaders();
    }
  });
  try {
    await page.goto("/");
    await page.getByRole("button", { name: `${previousEntry.name} ${previousEntry.relative_path}` }).click();
    await page.getByRole("button", { name: "载入更多", exact: true }).click();
    await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible({ timeout: 8_000 });
    await expect(page.getByText(previousEntry.name, { exact: true })).toHaveCount(0);
    await expect.poll(() => closedAfter.length).toBe(1);
    expect(closedAfter[0]).toBeGreaterThanOrEqual(4_000);
    expect(closedAfter[0]).toBeLessThan(8_000);
  } finally {
    await stop();
  }
});
