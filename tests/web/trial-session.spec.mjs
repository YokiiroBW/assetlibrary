import { expect, test } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import {
  browserSession,
  browsePage,
  browsePath,
  entry,
  entryDetail,
  entryOption,
  failure,
  libraryPage,
  libraryDetail,
  searchPage,
  visibleLibrary,
} from "./assetlink-fixtures.mjs";

async function sessionFixture(context, initial = null) {
  const state = {
    session: initial,
    sessionStatus: 200,
    logoutStatus: 204,
    loginStatus: 200,
    controls: [],
    authRequests: [],
  };
  await context.route("**/assetlink/v1/auth/*", async (route) => {
    const request = route.request();
    const operation = new URL(request.url()).pathname.split("/").at(-1);
    state.authRequests.push(operation);
    if (operation === "logout") {
      expect(request.headers()["x-assetlibrary-csrf"]).toBe(state.session.csrf_token);
      if (state.logoutStatus === 204) state.session = null;
      await route.fulfill({
        status: state.logoutStatus,
        contentType: "application/json",
        body:
          state.logoutStatus === 204
            ? ""
            : JSON.stringify({ code: "service_unavailable", message: "暂时无法确认退出。" }),
      });
      return;
    }
    if (operation === "login") {
      const body = request.postDataJSON();
      expect(Object.keys(body).sort()).toEqual(["account_name", "password"]);
      if (state.loginStatus !== 200 || body.password !== "fixture-passphrase-only") {
        await route.fulfill({
          status: state.loginStatus === 200 ? 401 : state.loginStatus,
          contentType: "application/json",
          headers: { "retry-after": "1" },
          body: JSON.stringify({
            code: "authentication_rejected",
            message: "登录失败，请检查账号和密码，或稍后重试。",
          }),
        });
        return;
      }
      state.session = {
        ...browserSession,
        principal_id: body.account_name === "second" ? "second-principal" : browserSession.principal_id,
        display_name: body.account_name === "second" ? "第二账号" : browserSession.display_name,
        csrf_token: `fixture-${body.account_name}-session`,
      };
    }
    const status =
      operation === "session" && state.sessionStatus !== 200 ? state.sessionStatus : state.session === null ? 401 : 200;
    await route.fulfill({
      status,
      contentType: "application/json",
      body: JSON.stringify(
        status === 200
          ? state.session
          : { code: status === 401 ? "authentication_required" : "service_unavailable", message: "暂时无法核验登录。" },
      ),
    });
  });
  await context.route("**/assetlink/v1/control", async (route) => {
    const request = route.request().postDataJSON();
    state.controls.push({ operation: request.operation, csrf: route.request().headers()["x-assetlibrary-csrf"] });
    let response;
    if (state.session === null) response = failure(request, 401, "authentication_required", "请重新登录。");
    else {
      expect(route.request().headers()["x-assetlibrary-csrf"]).toBe(state.session.csrf_token);
      const first = state.session.principal_id === browserSession.principal_id;
      const privateEntry = entry(991, {
        name: "first-account-private.png",
        relative_path: "first-account-private.png",
      });
      response =
        request.operation === "libraries.list"
          ? libraryPage(request, first ? [visibleLibrary] : [])
          : request.operation === "libraries.get"
            ? first
              ? libraryDetail(request)
              : failure(request, 404, "not_found", "资源库不可用。")
            : request.operation === "entries.get"
              ? first
                ? entryDetail(request, privateEntry)
                : failure(request, 404, "not_found", "条目不可用。")
              : request.operation === "assets.search"
                ? searchPage(
                    request,
                    first ? [{ library: visibleLibrary, entry: privateEntry, hit_reason: "name" }] : [],
                  )
                : browsePage(request, first ? [privateEntry] : [], "more-private");
    }
    await route.fulfill({
      status: response.status ?? 200,
      contentType: "application/json",
      body: JSON.stringify(response.body),
    });
  });
  return state;
}

async function signIn(page, account = "first") {
  await page.getByLabel("账号", { exact: true }).fill(account);
  await page.getByLabel("密码", { exact: true }).fill("fixture-passphrase-only");
  await page.getByRole("button", { name: "登录", exact: true }).click();
}

test("login uses password-manager fields and memory CSRF, then logout hides the workspace", async ({
  page,
  context,
}, testInfo) => {
  const state = await sessionFixture(context);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(browsePath);
  await expect(page.getByRole("heading", { name: "登录资源库" })).toBeVisible();
  await expect(page.getByLabel("账号", { exact: true })).toHaveAttribute("autocomplete", "username");
  await expect(page.getByLabel("密码", { exact: true })).toHaveAttribute("autocomplete", "current-password");
  await page.getByLabel("账号", { exact: true }).fill("first");
  await page.getByLabel("密码", { exact: true }).fill("wrong-fixture-password");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(page.getByRole("alert")).toBeFocused();
  await expect(page.getByLabel("密码", { exact: true })).toHaveValue("");
  await page.screenshot({ path: testInfo.outputPath("login-narrow.png"), animations: "disabled" });
  await signIn(page);
  await expect(page.getByText("first-account-private.png", { exact: true }).first()).toBeVisible();
  expect(state.controls.every((request) => request.csrf === state.session.csrf_token)).toBeTruthy();
  expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({
    local: 0,
    session: 0,
  });
  await expect(page.getByRole("button", { name: "添加资源库", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "退出登录" }).click();
  await expect(page.getByRole("heading", { name: "登录资源库" })).toBeVisible();
  await expect(page.getByText("first-account-private.png", { exact: true })).toHaveCount(0);
  expect(state.session).toBeNull();
});

test("failed logout is not confirmed and can be retried without exposing assets", async ({ page, context }) => {
  const state = await sessionFixture(context, browserSession);
  state.logoutStatus = 503;
  await page.goto(browsePath);
  await expect(page.getByText("first-account-private.png", { exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "退出登录" }).click();
  await expect(page.getByRole("heading", { name: "退出尚未确认" })).toBeVisible();
  await expect(page.getByText("first-account-private.png", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "登录资源库" })).toHaveCount(0);
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await expect(page.getByRole("heading", { name: "退出尚未确认" })).toBeVisible();
  state.logoutStatus = 204;
  await page.getByRole("button", { name: "重试退出" }).click();
  await expect(page.getByRole("heading", { name: "登录资源库" })).toBeVisible();
});

test("session outages stay distinct from expiration and recover without replacing the identity", async ({
  page,
  context,
}) => {
  const state = await sessionFixture(context, browserSession);
  state.sessionStatus = 503;
  await page.goto(browsePath);
  await expect(page.getByRole("heading", { name: "暂时无法连接" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "登录资源库" })).toHaveCount(0);
  state.sessionStatus = 200;
  await page.getByRole("button", { name: "重试连接" }).click();
  await expect(page.getByText("first-account-private.png", { exact: true }).first()).toBeVisible();
  state.sessionStatus = 503;
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await expect(page.getByRole("alert")).toContainText("暂时无法核验登录状态");
  await expect(page.getByText("first-account-private.png", { exact: true }).first()).toBeVisible();
  state.sessionStatus = 200;
  state.session = null;
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();
  await expect(page.getByText("first-account-private.png", { exact: true })).toHaveCount(0);
});

test("session changes across tabs discard previous libraries, search, details and cursors", async ({
  page,
  context,
}) => {
  await sessionFixture(context, browserSession);
  await page.goto(browsePath);
  await page.getByRole("searchbox").fill("private");
  await entryOption(page, "first-account-private.png").click();
  await expect(page.locator(".detail-pane")).toContainText("first-account-private.png");
  const other = await context.newPage();
  await other.goto("/");
  await other.getByRole("button", { name: "退出登录" }).click();
  await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();
  await signIn(other, "second");
  await expect(page.getByRole("heading", { name: "没有可见资源库" })).toBeVisible();
  await expect(page.getByRole("searchbox")).toHaveValue("");
  await expect(page.getByText("first-account-private.png", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("navigation", { name: "已加载资源库" }).getByRole("link")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "载入更多", exact: true })).toHaveCount(0);
  await expect(page.getByText("第二账号", { exact: true })).toBeVisible();
  await page.goBack();
  await expect(page.getByText("first-account-private.png", { exact: true })).toHaveCount(0);
});

test("focus revalidation detects a new session generation for the same principal", async ({ page, context }) => {
  const state = await sessionFixture(context, browserSession);
  await page.goto(browsePath);
  await page.getByRole("searchbox").fill("private");
  await expect(page.getByText("first-account-private.png", { exact: true }).first()).toBeVisible();
  state.session = { ...browserSession, csrf_token: "replacement-fixture-session" };
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await expect(page.getByRole("searchbox")).toHaveValue("");
  await expect.poll(() => state.controls.at(-1)?.csrf).toBe("replacement-fixture-session");
  const sessionReads = state.authRequests.filter((operation) => operation === "session").length;
  state.session = null;
  await page.evaluate(() => window.dispatchEvent(new PageTransitionEvent("pageshow", { persisted: true })));
  await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();
  expect(state.authRequests.filter((operation) => operation === "session").length).toBeGreaterThan(sessionReads);
  await expect(page.getByText("first-account-private.png", { exact: true })).toHaveCount(0);
});

test("absolute session expiry hides data without waiting for another asset request", async ({ page, context }) => {
  await sessionFixture(context, { ...browserSession, absolute_expires_at: new Date(Date.now() + 1_500).toISOString() });
  await page.goto(browsePath);
  await expect(page.getByText("first-account-private.png", { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("heading", { name: "登录状态已失效" })).toBeVisible();
  await expect(page.getByText("first-account-private.png", { exact: true })).toHaveCount(0);
});

test("rate-limited login stays a retryable form with no workspace", async ({ page, context }) => {
  const state = await sessionFixture(context);
  state.loginStatus = 429;
  await page.goto(browsePath);
  await signIn(page);
  await expect(page.getByRole("alert")).toContainText("稍后重试");
  await expect(page.getByRole("button", { name: "登录", exact: true })).toBeEnabled();
  await expect(page.getByRole("searchbox")).toHaveCount(0);
  expect(state.controls).toHaveLength(0);
});

for (const status of [403, 404]) {
  test(`a ${status} session rejection on focus hides the previous workspace`, async ({ page, context }) => {
    const state = await sessionFixture(context, browserSession);
    await page.goto(browsePath);
    await expect(page.getByText("first-account-private.png", { exact: true }).first()).toBeVisible();
    state.sessionStatus = status;
    await page.evaluate(() => window.dispatchEvent(new Event("focus")));
    await expect(page.getByRole("heading", { name: "暂时无法连接" })).toBeVisible();
    await expect(page.getByText("first-account-private.png", { exact: true })).toHaveCount(0);
  });
}
