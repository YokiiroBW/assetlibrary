import { expect, test } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import {
  browserSession,
  browsePath,
  entry,
  entryOption,
  mockSession,
  visibleLibrary,
  browsePage,
  libraryPage,
  libraryDetail,
  failure,
  mockAssetLink,
} from "./assetlink-fixtures.mjs";
import { imagePattern, imageWorkspace, png, servePng, startImageServer } from "./image-fixtures.mjs";

test("derived thumbnails use only the visible range, cancel on scroll, and reacquire on return", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const items = Array.from({ length: 100 }, (_, i) => entry(i + 1));
  await imageWorkspace(page, items);
  const requested = [];
  let concurrent = 0;
  let peak = 0;
  const stop = await startImageServer(page, (request, response) => {
    const id = request.url.split("/entries/")[1].split("/")[0];
    requested.push(id);
    concurrent++;
    peak = Math.max(peak, concurrent);
    response.on("close", () => concurrent--);
    const body = png();
    setTimeout(() => {
      if (!response.destroyed) {
        response.setHeader("content-type", "image/png");
        response.setHeader("content-length", body.length);
        response.end(body);
      }
    }, 80);
  });
  try {
    await page.goto(`${browsePath}?view=grid`);
    await expect(page.locator(".image-thumbnail img").first()).toBeVisible();
    await expect.poll(() => requested.length).toBeGreaterThan(3);
    expect(requested).not.toContain(items[70].entry_id);
    expect(peak).toBeLessThanOrEqual(2);
    const firstReads = requested.filter((id) => id === items[0].entry_id).length;
    await page.locator(".entry-scroll").evaluate((element) => {
      element.scrollTop = element.scrollHeight;
    });
    await expect.poll(() => requested.includes(items[99].entry_id)).toBeTruthy();
    await page.locator(".entry-scroll").evaluate((element) => {
      element.scrollTop = 0;
    });
    await expect.poll(() => requested.filter((id) => id === items[0].entry_id).length).toBeGreaterThan(firstReads);
    expect(peak).toBeLessThanOrEqual(2);
    expect(await page.evaluate(() => window.liveImageUrls.size)).toBeLessThanOrEqual(47);
  } finally {
    await stop();
  }
});

for (const viewport of [
  { width: 1440, height: 900 },
  { width: 390, height: 844 },
]) {
  test(`image preview preserves Quick Look, history, focus and responsive layout at ${viewport.width}`, async ({
    page,
  }, testInfo) => {
    await page.setViewportSize(viewport);
    await page.emulateMedia({ colorScheme: viewport.width === 390 ? "dark" : "light", reducedMotion: "reduce" });
    const item = entry(10, { name: "中文透明图-with-a-long-name.png" });
    await imageWorkspace(page, [item]);
    await page.route(imagePattern, (route) =>
      servePng(route, route.request().url().endsWith("preview") ? png(1200, 800) : png()),
    );
    await page.goto(`${browsePath}?view=grid`);
    const row = entryOption(page, item.name);
    await expect(row.locator("img")).toBeVisible();
    await row.click();
    const before = await page.evaluate(() => ({ url: location.href, history: history.length }));
    await page.keyboard.press("Space");
    const quick = page.getByRole("dialog", { name: "快速查看" });
    await expect(quick.getByRole("img", { name: item.name })).toBeVisible();
    expect(await page.evaluate(() => ({ url: location.href, history: history.length }))).toEqual(before);
    await page.keyboard.press("Escape");
    await expect(quick).toHaveCount(0);
    await expect(row).toBeFocused();
    await page.keyboard.press("Enter");
    const dialog = page.getByRole("dialog", { name: "图片预览" });
    const image = dialog.getByRole("img", { name: item.name });
    await expect(image).toBeVisible();
    expect(await image.evaluate((element) => [element.naturalWidth, element.naturalHeight])).toEqual([1200, 800]);
    await expect(page).toHaveURL(new RegExp(`entry=${item.entry_id}`));
    for (let i = 0; i < 5; i++) {
      await page.keyboard.press("Tab");
      expect(await dialog.evaluate((element) => element.contains(document.activeElement))).toBeTruthy();
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
    await page.screenshot({ path: testInfo.outputPath(`preview-${viewport.width}.png`), animations: "disabled" });
    if (viewport.width === 390) {
      await page.evaluate(() => {
        document.documentElement.style.fontSize = "32px";
      });
      await expect(dialog.getByRole("button", { name: "关闭图片预览" })).toBeVisible();
      const closeBounds = await dialog.getByRole("button", { name: "关闭图片预览" }).boundingBox();
      expect(closeBounds.x + closeBounds.width).toBeLessThanOrEqual(viewport.width);
      expect(closeBounds.y).toBeGreaterThanOrEqual(0);
      await page.screenshot({ path: testInfo.outputPath("preview-mobile-200.png"), animations: "disabled" });
      await page.evaluate(() => {
        document.documentElement.style.fontSize = "";
      });
    }
    await page.goBack();
    await expect(dialog).toHaveCount(0);
    await expect(row).toBeFocused();
    await page.goForward();
    await expect(image).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(page).not.toHaveURL(/entry=/);
  });
}

test("foreground revalidation and identity changes revoke every image URL before reuse", async ({ page }) => {
  const item = entry(20);
  await imageWorkspace(page, [item]);
  let reads = 0;
  await page.route(imagePattern, (route) => {
    reads++;
    return servePng(route);
  });
  await page.goto(browsePath);
  await expect(page.locator(".image-thumbnail img")).toBeVisible();
  const previous = reads;
  await page.evaluate(() => window.dispatchEvent(new Event("blur")));
  await expect(page.locator(".image-thumbnail img")).toHaveCount(0);
  expect(await page.evaluate(() => window.liveImageUrls.size)).toBe(0);
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await expect(page.locator(".image-thumbnail img")).toBeVisible();
  expect(reads).toBeGreaterThan(previous);
  await mockSession(page, {
    ...browserSession,
    principal_id: "77777777-7777-4777-8777-777777777777",
    csrf_token: "next-session",
  });
  await page.evaluate(() => {
    const channel = new BroadcastChannel("assetlibrary-session");
    channel.postMessage("session-changed");
    channel.close();
  });
  await expect(page).toHaveURL("/");
  await expect(page.locator("img[src^='blob:']")).toHaveCount(0);
  expect(await page.evaluate(() => window.liveImageUrls.size)).toBe(0);
});

test("keyboard focus follows selection between already mounted virtual rows", async ({ page }) => {
  const items = [entry(21), entry(22), entry(23)];
  await imageWorkspace(page, items);
  await page.goto(browsePath);
  await entryOption(page, items[0].name).click();
  await page.keyboard.press("ArrowRight");
  await expect(entryOption(page, items[1].name)).toHaveAttribute("aria-selected", "true");
  await expect(entryOption(page, items[1].name)).toBeFocused();
  await page.keyboard.press("ArrowRight");
  await expect(entryOption(page, items[2].name)).toBeFocused();
});

test("image admission bounds its queue and expires queued work within the original budget", async ({ page }) => {
  await imageWorkspace(page, []);
  await page.clock.install();
  await page.goto("/");
  await page.evaluate(async () => {
    const { ImageRequests } = await import("/src/imageRequests.ts");
    window.admission = { started: 0, busy: 0, timeouts: 0 };
    const client = {
      getImage: (_library, _entry, _variant, signal) => {
        window.admission.started++;
        return new Promise((_resolve, reject) =>
          signal.addEventListener("abort", () => reject(signal.reason), { once: true }),
        );
      },
    };
    window.imagePool = new ImageRequests(client, () => {});
    const observe = (state) => {
      if (state.statusCode === 429) window.admission.busy++;
      if (state.statusCode === 504) window.admission.timeouts++;
    };
    for (let i = 0; i < 60; i++) window.imagePool.acquire("fixture-library", String(i), "thumbnail", observe);
    window.imagePool.acquire("fixture-library", "opened", "preview", observe);
  });
  expect(await page.evaluate(() => window.admission)).toEqual({ started: 2, busy: 13, timeouts: 0 });
  await page.clock.fastForward(20_000);
  await expect.poll(() => page.evaluate(() => window.admission.timeouts)).toBe(48);
  expect(await page.evaluate(() => window.admission.started)).toBe(2);
  await page.evaluate(() => window.imagePool.dispose());
});

test("image leases enforce the total decoded pixel budget and dispose successful URLs", async ({ page }) => {
  await imageWorkspace(page, []);
  await page.goto("/");
  const result = await page.evaluate(
    async (bytes) => {
      const { ImageRequests } = await import("/src/imageRequests.ts");
      const blob = new Blob([new Uint8Array(bytes)], { type: "image/png" });
      const pool = new ImageRequests({ getImage: async () => ({ blob, width: 512, height: 512 }) }, () => {});
      const states = await Promise.all(
        Array.from(
          { length: 47 },
          (_, index) =>
            new Promise((resolve) => {
              pool.acquire("fixture-library", String(index), "thumbnail", (state) => {
                if (state.status === "ready" || state.status === "error") resolve(state);
              });
            }),
        ),
      );
      const ready = states.filter((state) => state.status === "ready").length;
      const limited = states.filter((state) => state.status === "error" && state.statusCode === 422).length;
      const urls = window.liveImageUrls.size;
      pool.dispose();
      return { ready, limited, urls, remaining: window.liveImageUrls.size };
    },
    [...png(512, 512)],
  );
  expect(result).toEqual({ ready: 45, limited: 2, urls: 45, remaining: 0 });
  expect(result.ready * 512 * 512).toBeLessThanOrEqual(12_000_000);
});

test("busy Retry-After shares one total deadline and never starts a fresh retry budget", async ({ page }) => {
  await imageWorkspace(page, []);
  await page.clock.install();
  let attempts = 0;
  await page.route(imagePattern, (route) => {
    attempts++;
    return route.fulfill({ status: 429, headers: { "retry-after": "60" }, body: "{}" });
  });
  await page.goto("/");
  await page.evaluate(
    async ({ libraryId, entryId }) => {
      const { AssetLinkClient } = await import("/src/assetLinkClient.ts");
      window.busyResult = new AssetLinkClient()
        .getImage(libraryId, entryId, "preview", new AbortController().signal)
        .catch((error) => error.code);
    },
    { libraryId: visibleLibrary.library_id, entryId: entry(81).entry_id },
  );
  await expect.poll(() => attempts).toBe(1);
  await page.clock.fastForward(20_000);
  expect(await page.evaluate(() => window.busyResult)).toBe("preview_timeout");
  expect(attempts).toBe(1);
});

test("reopened images reflect new server bytes and BFCache revalidation removes sensitive pixels", async ({ page }) => {
  const item = entry(90, { name: "image-without-extension" });
  await imageWorkspace(page, [item]);
  let updated = false;
  await page.route(imagePattern, (route) => servePng(route, updated ? png(160, 100, 90) : png()));
  await page.goto(browsePath);
  await expect(page.locator(".image-thumbnail img")).toBeVisible();
  await entryOption(page, item.name).dblclick();
  await expect(page.locator(".image-preview img")).toHaveJSProperty("naturalWidth", 320);
  await page.keyboard.press("Escape");
  updated = true;
  await entryOption(page, item.name).dblclick();
  await expect(page.locator(".image-preview img")).toHaveJSProperty("naturalWidth", 160);
  await page.evaluate(() => window.dispatchEvent(new PageTransitionEvent("pagehide", { persisted: true })));
  expect(await page.evaluate(() => window.liveImageUrls.size)).toBe(0);
  await page.route("**/assetlink/v1/auth/session", (route) =>
    route.fulfill({
      status: 401,
      contentType: "application/json",
      body: JSON.stringify({ code: "unauthenticated", message: "expired" }),
    }),
  );
  await page.evaluate(() => window.dispatchEvent(new PageTransitionEvent("pageshow", { persisted: true })));
  await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();
  expect(await page.evaluate(() => window.liveImageUrls.size)).toBe(0);
});

test("directories and reparse entries never request images", async ({ page }) => {
  const items = [
    entry(91, { kind: "directory", content_length: null }),
    entry(92, { kind: "reparse_file" }),
    entry(93, { kind: "reparse_directory", content_length: null }),
  ];
  await imageWorkspace(page, items);
  let reads = 0;
  await page.route(imagePattern, (route) => {
    reads++;
    return servePng(route);
  });
  await page.goto(browsePath);
  await entryOption(page, items[1].name).dblclick();
  await expect(page.getByRole("dialog").getByText("此条目仅提供文件信息。")).toBeVisible();
  expect(reads).toBe(0);
});

test("a rejected L0 detail clears thumbnails even when the image endpoint has not rejected yet", async ({ page }) => {
  const item = entry(94);
  await imageWorkspace(page, [item]);
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request);
    if (request.operation === "entries.get") return failure(request, 404, "not_found", "unavailable");
    return browsePage(request, [item]);
  });
  await page.route(imagePattern, (route) => servePng(route));
  await page.goto(browsePath);
  await expect(page.locator(".image-thumbnail img")).toBeVisible();
  await entryOption(page, item.name).click();
  await expect(page.locator(".image-thumbnail img")).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => window.liveImageUrls.size)).toBe(0);
});

for (const status of [401, 403, 404]) {
  test(`image ${status} acts before a stalled error body and clears existing images`, async ({ page }) => {
    const items = [entry(30), entry(31)];
    await imageWorkspace(page, items);
    let reject = false;
    const stop = await startImageServer(page, (_request, response) => {
      if (reject) {
        response.statusCode = status;
        response.writeHead(status, { "content-type": "application/json" });
        response.write('{"code":');
      } else {
        const body = png();
        response.writeHead(200, { "content-type": "image/png", "content-length": body.length });
        response.end(body);
      }
    });
    try {
      await page.goto(browsePath);
      await expect(page.locator(".image-thumbnail img")).toHaveCount(2);
      reject = true;
      await entryOption(page, items[0].name).dblclick();
      if (status === 401)
        await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible({ timeout: 2000 });
      else
        await expect(page.getByRole("dialog").getByText("图片不可访问，仍可核对文件信息。")).toBeVisible({
          timeout: 2000,
        });
      expect(await page.evaluate(() => window.liveImageUrls.size)).toBe(0);
      await expect(page.locator("img[src^='blob:']")).toHaveCount(0);
    } finally {
      await stop();
    }
  });
}

for (const [status, code, message] of [
  [409, "source_changed", "原文件已变化"],
  [415, "preview_unsupported", "暂不支持图片预览"],
  [422, "preview_invalid", "图片已损坏"],
  [422, "preview_limit_exceeded", "超出预览大小或像素限制"],
  [503, "preview_unavailable", "图片预览暂不可用"],
]) {
  test(`image ${code} retains L0 and has an explicit retry without auto looping`, async ({ page }) => {
    const item = entry(40);
    await imageWorkspace(page, [item]);
    let attempts = 0;
    await page.route(imagePattern, (route) => {
      attempts++;
      return route.fulfill({
        status,
        contentType: "application/json",
        body: JSON.stringify({ code, message: "private decoder path must not appear" }),
      });
    });
    await page.goto(`${browsePath}?entry=${item.entry_id}`);
    const dialog = page.getByRole("dialog", { name: "图片预览" });
    await expect(dialog.getByText(new RegExp(message))).toBeVisible();
    const count = attempts;
    await dialog.locator("summary").click();
    await expect(dialog.getByRole("button", { name: "定位所在目录" })).toBeVisible();
    await expect(page.getByText("private decoder path must not appear")).toHaveCount(0);
    await dialog.getByRole("button", { name: "重试图片" }).click();
    await expect.poll(() => attempts).toBe(count + 1);
  });
}

test("malformed MIME, PNG dimensions and encoded lengths never reach image rendering", async ({ page }) => {
  const item = entry(50);
  await imageWorkspace(page, [item]);
  const cases = [
    { contentType: "image/svg+xml", body: Buffer.from("<svg/>") },
    { contentType: "image/png", body: png(1601, 1) },
    { contentType: "image/png", body: png(), length: "12582913" },
    { contentType: "image/png", body: Buffer.alloc(64) },
    { contentType: "image/png", body: png().subarray(0, 40) },
  ];
  for (const sample of cases) {
    await page.route(imagePattern, (route) =>
      route.fulfill({
        status: 200,
        contentType: sample.contentType,
        headers: { "content-length": sample.length ?? String(sample.body.length) },
        body: sample.body,
      }),
    );
    await page.goto(`${browsePath}?entry=${item.entry_id}`);
    await expect(page.getByRole("button", { name: "重试图片" })).toBeVisible();
    await expect(page.locator(".image-preview img")).toHaveCount(0);
    expect(await page.evaluate(() => window.liveImageUrls.size)).toBe(0);
  }
});

test("busy image retries are capped at two and respect Retry-After", async ({ page }) => {
  const item = entry(60);
  await imageWorkspace(page, [item]);
  const times = [];
  await page.route(imagePattern, (route) => {
    times.push(Date.now());
    return route.fulfill({
      status: 429,
      headers: { "retry-after": "1" },
      contentType: "application/json",
      body: JSON.stringify({ code: "preview_busy" }),
    });
  });
  await page.goto("/");
  const result = await page.evaluate(
    async ({ libraryId, entryId }) => {
      const { AssetLinkClient } = await import("/src/assetLinkClient.ts");
      try {
        await new AssetLinkClient().getImage(libraryId, entryId, "preview", new AbortController().signal);
      } catch (error) {
        return error.code;
      }
    },
    { libraryId: visibleLibrary.library_id, entryId: item.entry_id },
  );
  expect(result).toBe("preview_busy");
  expect(times).toHaveLength(3);
  expect(times[1] - times[0]).toBeGreaterThanOrEqual(900);
  expect(times[2] - times[1]).toBeGreaterThanOrEqual(900);
});

test("closing a pending preview cancels the connection and suppresses late image publication", async ({ page }) => {
  const item = entry(70);
  await imageWorkspace(page, [item]);
  let previewStarted = false;
  let previewClosed = false;
  const stop = await startImageServer(page, (request, response) => {
    if (request.url.endsWith("preview")) {
      previewStarted = true;
      response.on("close", () => {
        previewClosed = true;
      });
    } else {
      const body = png();
      response.writeHead(200, { "content-type": "image/png", "content-length": body.length });
      response.end(body);
    }
  });
  try {
    await page.goto(browsePath);
    await expect(page.locator(".image-thumbnail img")).toBeVisible();
    await entryOption(page, item.name).dblclick();
    await expect.poll(() => previewStarted).toBeTruthy();
    await page.keyboard.press("Escape");
    await expect.poll(() => previewClosed).toBeTruthy();
    await expect(page.locator(".image-preview img")).toHaveCount(0);
    await expect(page.locator(".image-thumbnail img")).toBeVisible();
    expect(await page.evaluate(() => window.liveImageUrls.size)).toBe(1);
  } finally {
    await stop();
  }
});

test("an image stalled body uses the 20 second budget without extending JSON deadlines", async ({ page }) => {
  test.setTimeout(35_000);
  await imageWorkspace(page, []);
  let closedAfter = null;
  const stop = await startImageServer(page, (_request, response) => {
    const started = Date.now();
    response.on("close", () => {
      closedAfter = Date.now() - started;
    });
    response.writeHead(200, { "content-type": "image/png", "content-length": "2000" });
    response.write(png().subarray(0, 32));
  });
  try {
    await page.goto("/");
    const result = await page.evaluate(
      async ({ libraryId, entryId }) => {
        const { AssetLinkClient } = await import("/src/assetLinkClient.ts");
        try {
          await new AssetLinkClient().getImage(libraryId, entryId, "preview", new AbortController().signal);
          return "unexpected success";
        } catch (error) {
          return error.code;
        }
      },
      { libraryId: visibleLibrary.library_id, entryId: entry(80).entry_id },
    );
    expect(result).toBe("preview_timeout");
    await expect.poll(() => closedAfter).not.toBeNull();
    expect(closedAfter).toBeGreaterThanOrEqual(19_000);
    expect(closedAfter).toBeLessThan(24_000);
  } finally {
    await stop();
  }
});
