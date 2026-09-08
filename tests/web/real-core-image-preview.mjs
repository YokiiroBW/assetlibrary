import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, isAbsolute, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";
import { chromium, expect } from "../../apps/web/node_modules/@playwright/test/index.mjs";
import {
  corpusCases,
  discoverCorpusEntries,
  inspectDerivedPng,
  readBoundedJson,
  validateConnection,
  validateCorpus,
  verifyFixtureTls,
} from "./real-core-image-support.mjs";

const { values } = parseArgs({
  options: {
    execute: { type: "boolean", default: false },
    help: { type: "boolean", default: false },
    connection: { type: "string" },
    manifest: { type: "string" },
    evidence: { type: "string" },
    "build-evidence": { type: "string" },
  },
});
if (values.help || !values.execute) {
  process.stdout.write(
    "Real Core image acceptance: --execute --connection <private connection.json> --manifest <coordinator manifest.json> --evidence <new .runtime directory> [--build-evidence <reviewed build-evidence.json>]\n",
  );
  process.exit(values.help ? 0 : 77);
}

const root = fileURLToPath(new URL("../../", import.meta.url));
const receipt = {
  status: "failed",
  source: "real_core_https",
  cases: [],
  screenshots: [],
  browser_closed: false,
  shared_server_lifecycle: "owned_and_verified_by_coordinator",
};
let stage = "validate_inputs";
let evidence;
let browser;
let deadline;
let timedOut = false;
let abortAcceptance;
try {
  await Promise.race([
    (async () => {
      assert.equal(process.versions.node, "24.20.0", "use the repository-pinned Node runtime");
      assert.ok(values.connection && values.manifest && values.evidence, "all explicit rendezvous inputs are required");
      const suffix = relative(join(root, ".runtime"), resolve(values.evidence));
      assert.ok(
        suffix && !suffix.startsWith("..") && !isAbsolute(suffix),
        "evidence must be inside this worktree .runtime",
      );
      const candidateEvidence = resolve(values.evidence);
      await mkdir(dirname(candidateEvidence), { recursive: true });
      await mkdir(candidateEvidence);
      evidence = candidateEvidence;
      await checkpoint("read_private_connection");
      const connection = validateConnection(await readBoundedJson(values.connection, 16_384));
      await checkpoint("read_public_inputs");
      const manifest = validateCorpus(await readBoundedJson(values.manifest, 131_072));
      const build = await readBoundedJson(
        values["build-evidence"] ?? join(root, ".codex/handoffs/V03-008/build-evidence.json"),
        65_536,
      );
      await checkpoint("record_provenance");
      receipt.runner_commit = execFileSync("git", ["rev-parse", "HEAD"], {
        cwd: root,
        encoding: "utf8",
        timeout: 5000,
        windowsHide: true,
        stdio: ["ignore", "pipe", "pipe"],
      }).trim();
      receipt.web_source_commit = build.source_commit;
      receipt.corpus_manifest_sha256 = createHash("sha256").update(JSON.stringify(manifest)).digest("hex");
      receipt.origin = connection.origin;
      await checkpoint("verify_tls_identity");
      const spki = await verifyFixtureTls(connection);
      receipt.tls = "exact_leaf_sha256_localhost_validity_verified_before_credentials";
      await checkpoint("launch_browser");
      browser = await chromium.launch({ headless: true, args: [`--ignore-certificate-errors-spki-list=${spki}`] });
      if (timedOut) {
        await browser.close();
        throw new Error("bounded_run_deadline");
      }
      clearTimeout(deadline);
      deadline = setTimeout(
        () => {
          timedOut = true;
          abortAcceptance(new Error("bounded_run_deadline"));
        },
        Math.min(300_000, Date.parse(connection.expires_at) - Date.now() - 10_000),
      );
      const context = await browser.newContext({
        viewport: { width: 1440, height: 900 },
        locale: "zh-CN",
        timezoneId: "Asia/Shanghai",
        reducedMotion: "reduce",
        ignoreHTTPSErrors: false,
        bypassCSP: false,
      });
      context.setDefaultTimeout(25_000);
      context.setDefaultNavigationTimeout(25_000);
      const page = await context.newPage();
      const responses = [];
      const foreignRequests = [];
      let scriptDialogs = 0;
      page.on("response", (response) => {
        if (responses.length < 300 && new URL(response.url()).origin === connection.origin) responses.push(response);
      });
      page.on("request", (request) => {
        const url = new URL(request.url());
        if (
          ["http:", "https:"].includes(url.protocol) &&
          url.origin !== connection.origin &&
          foreignRequests.length < 10
        )
          foreignRequests.push(url.origin);
      });
      page.on("dialog", (dialog) => {
        scriptDialogs++;
        void dialog.dismiss();
      });
      await page.addInitScript(() => {
        window.previewAcceptance = { urls: new Set(), violations: [] };
        const create = URL.createObjectURL.bind(URL),
          revoke = URL.revokeObjectURL.bind(URL);
        URL.createObjectURL = (blob) => {
          const url = create(blob);
          window.previewAcceptance.urls.add(url);
          return url;
        };
        URL.revokeObjectURL = (url) => {
          window.previewAcceptance.urls.delete(url);
          revoke(url);
        };
        document.addEventListener("securitypolicyviolation", (event) => {
          if (window.previewAcceptance.violations.length < 20)
            window.previewAcceptance.violations.push(event.effectiveDirective);
        });
      });

      await checkpoint("load_reviewed_web");
      const documentResponse = await page.goto(connection.origin);
      assert.equal(documentResponse.status(), 200, "real Host must serve the Web shell");
      const csp = documentResponse.headers()["content-security-policy"] ?? "";
      const imageDirective = csp
        .split(";")
        .map((part) => part.trim().split(/\s+/))
        .find((part) => part[0] === "img-src");
      assert.ok(imageDirective?.includes("blob:"), "real Host must permit blob images through its image CSP");
      await expect(page.getByRole("heading", { name: "登录资源库", exact: true })).toBeVisible();
      for (const artifact of build.artifacts) {
        const target = new URL(`/assets/${artifact.path.split("/").at(-1)}`, connection.origin).href;
        const response = responses.find((item) => item.url() === target && item.status() === 200);
        assert.ok(response, "Host must serve the reviewed JS/CSS asset");
        const bytes = await response.body();
        assert.equal(bytes.length, artifact.bytes, "served artifact size differs from reviewed build");
        assert.equal(
          createHash("sha256").update(bytes).digest("hex"),
          artifact.sha256,
          "served artifact hash differs from reviewed build",
        );
      }
      receipt.reviewed_web_assets = "matched_exact_sha256";
      await checkpoint("administrator_sign_in");
      await signIn(page, connection.account_name, connection.password);

      await checkpoint("discover_real_entries");
      await page.goto(
        new URL(
          `/libraries/${connection.library_id}?path=${encodeURIComponent("图片样例")}&view=grid`,
          connection.origin,
        ).href,
      );
      await expect(page.getByRole("heading", { name: "图片样例", exact: true })).toBeVisible();
      const searchResponse = page.waitForResponse(
        (response) =>
          response.url().endsWith("/assetlink/v1/control") &&
          response.request().postDataJSON()?.operation === "assets.search",
      );
      await page.getByRole("searchbox").fill("图片样例");
      const searched = await searchResponse;
      assert.equal(searched.status(), 200, "authorized discovery query must succeed");
      const searchResult = await searched.json();
      assert.equal(
        searchResult.request_id,
        searched.request().postDataJSON().request_id,
        "query correlation must match",
      );
      assert.equal(searchResult.message_type, "control.result");
      assert.equal(searchResult.body.next_cursor, null, "the corpus query must fit one bounded page");
      const entries = discoverCorpusEntries(searchResult.body.items, connection.library_id);
      receipt.discovery = { total_hits: searchResult.body.items.length, corpus_files: entries.size };
      const option = (sample) =>
        page
          .getByRole("listbox", { name: "资产列表" })
          .locator(`[data-entry-id="${entries.get(`图片样例/${sample.path}`).entry_id}"]`);

      for (const sample of corpusCases) {
        const entry = entries.get(`图片样例/${sample.path}`);
        await checkpoint(`case:${sample.path}`);
        const row = option(sample);
        await row.scrollIntoViewIfNeeded();
        if (sample.result === "image") {
          await expect(row.locator(".image-thumbnail img")).toBeVisible({ timeout: 25_000 });
          const thumb = await imageEvidence(responses, connection, entry.entry_id, "thumbnail", sample);
          receipt.cases.push({ case: sample.path, variant: "thumbnail", ...thumb });
        }
        const before = page.url();
        await row.dblclick();
        const dialog = page.getByRole("dialog", { name: "图片预览", exact: true });
        await expect(dialog).toBeVisible();
        if (sample.result === "image") {
          const image = dialog.getByRole("img", { name: entry.name, exact: true });
          await expect(image).toBeVisible({ timeout: 25_000 });
          const preview = await imageEvidence(responses, connection, entry.entry_id, "preview", sample);
          assert.deepEqual(await image.evaluate((element) => [element.naturalWidth, element.naturalHeight]), [
            preview.width,
            preview.height,
          ]);
          const alpha = await image.evaluate((element) => {
            const canvas = document.createElement("canvas");
            canvas.width = element.naturalWidth;
            canvas.height = element.naturalHeight;
            const painter = canvas.getContext("2d");
            painter.drawImage(element, 0, 0);
            return {
              corner: painter.getImageData(0, 0, 1, 1).data[3],
              center: painter.getImageData(Math.floor(canvas.width / 2), Math.floor(canvas.height / 2), 1, 1).data[3],
            };
          });
          if (sample.alpha)
            assert.ok(
              alpha.corner === 0 && alpha.center > 0 && alpha.center < 255,
              "real derived image must retain alpha",
            );
          receipt.cases.push({ case: sample.path, variant: "preview", ...preview, ...(sample.alpha ? { alpha } : {}) });
          if (sample.path === "landscape.jpg") await screenshot(page, evidence, receipt, "real-preview-desktop.png");
          if (sample.alpha) {
            await page.setViewportSize({ width: 390, height: 844 });
            await page.emulateMedia({ colorScheme: "dark", reducedMotion: "reduce" });
            await expect(image).toBeVisible();
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
            await screenshot(page, evidence, receipt, "real-preview-mobile-alpha.png");
            await page.evaluate(() => {
              document.documentElement.style.fontSize = "32px";
            });
            await screenshot(page, evidence, receipt, "real-preview-mobile-text200.png");
            await page.evaluate(() => {
              document.documentElement.style.fontSize = "";
            });
            await page.setViewportSize({ width: 1440, height: 900 });
            await page.emulateMedia({ colorScheme: "light" });
          }
        } else {
          await expect(dialog.getByRole("button", { name: "重试图片", exact: true })).toBeVisible({ timeout: 25_000 });
          const response = await completedImageResponse(responses, connection, entry.entry_id, "preview");
          assert.ok(
            sample.status.includes(response.status()),
            "unsafe input must fail with the contract status, not engine unavailability",
          );
          const body = await response.body();
          assert.ok(body.length <= 8192, "error response must be bounded");
          const error = JSON.parse(body);
          assert.ok(sample.codes.includes(error.code), "error code must identify the supported failure boundary");
          await expect(dialog.locator(".image-preview img")).toHaveCount(0);
          await dialog.locator("summary").click();
          await expect(dialog.getByRole("button", { name: "定位所在目录", exact: true })).toBeVisible();
          receipt.cases.push({
            case: sample.path,
            variant: "preview",
            http_status: response.status(),
            code: error.code,
            l0_retained: true,
          });
        }
        await page.keyboard.press("Escape");
        await expect(dialog).toHaveCount(0);
        await expect(page).toHaveURL(before);
        await expect(row).toBeFocused();
      }
      const jpeg = receipt.cases.find((item) => item.case === "landscape.jpg" && item.variant === "preview");
      const renamed = receipt.cases.find((item) => item.case === "中文目录/重复内容.dat" && item.variant === "preview");
      assert.equal(
        jpeg.sha256,
        renamed.sha256,
        "identical source bytes under a Unicode .dat name must produce the same derivative",
      );

      await checkpoint("real_quick_look");
      const transparent = corpusCases.find((sample) => sample.alpha);
      await option(transparent).click();
      const previousHistory = await page.evaluate(() => ({ href: location.href, length: history.length }));
      await page.keyboard.press("Space");
      const quick = page.getByRole("dialog", { name: "快速查看", exact: true });
      await expect(quick.locator("img")).toBeVisible({ timeout: 25_000 });
      assert.deepEqual(await page.evaluate(() => ({ href: location.href, length: history.length })), previousHistory);
      await page.keyboard.press("Escape");
      await expect(quick).toHaveCount(0);
      receipt.quick_look_history = "unchanged";

      await checkpoint("logout_and_invisible_identity");
      await signOut(page);
      assert.equal(
        await page.evaluate(() => window.previewAcceptance.urls.size),
        0,
        "logout must release all identity image URLs",
      );
      await signIn(page, connection.invisible_account_name, connection.invisible_account_password);
      await expect(page.getByRole("heading", { name: "没有可见资源库", exact: true })).toBeVisible();
      const knownEntry = entries.get("图片样例/transparent.png").entry_id;
      const imagePath = `/assetlink/v1/libraries/${connection.library_id}/entries/${knownEntry}/image?variant=preview`;
      const forbidden = await page.evaluate(async (path) => {
        const response = await fetch(path, { credentials: "same-origin", cache: "no-store", redirect: "error" });
        return { status: response.status, mime: response.headers.get("content-type") };
      }, imagePath);
      assert.equal(forbidden.status, 404, "the other identity must not read known image IDs");
      assert.ok(!forbidden.mime?.startsWith("image/"), "denied identity must not receive image bytes");
      await expect(page.locator("img[src^='blob:']")).toHaveCount(0);
      await screenshot(page, evidence, receipt, "real-invisible-identity.png");
      await signOut(page);
      const anonymous = await page.evaluate(
        async (path) =>
          (await fetch(path, { credentials: "same-origin", cache: "no-store", redirect: "error" })).status,
        imagePath,
      );
      assert.equal(anonymous, 401, "logged-out image requests must be rejected");
      receipt.permission_checks = {
        invisible_identity: forbidden.status,
        anonymous,
        live_grant_mutation: "not_performed_client_never_writes_database",
      };
      assert.equal(await page.evaluate(() => window.previewAcceptance.urls.size), 0);
      assert.deepEqual(
        await page.evaluate(() => window.previewAcceptance.violations),
        [],
        "real CSP must not block rendered previews",
      );
      assert.equal(scriptDialogs, 0, "unsafe source content must never execute a script");
      assert.deepEqual(foreignRequests, [], "the acceptance flow must stay on its exact fixture origin");
      receipt.browser_version = browser.version();
      receipt.csp_blob_rendering = "verified_without_bypass";
      assert.ok(!timedOut, "bounded run deadline reached");
      receipt.status = "passed";
    })(),
    new Promise((_, reject) => {
      abortAcceptance = reject;
      deadline = setTimeout(() => {
        timedOut = true;
        reject(new Error("bounded_run_deadline"));
      }, 300_000);
    }),
  ]);
} catch (error) {
  receipt.failure = {
    stage,
    reason: timedOut
      ? "bounded_run_deadline"
      : error?.name === "AssertionError"
        ? "assertion_failed"
        : "execution_failed",
  };
  process.exitCode = 1;
} finally {
  clearTimeout(deadline);
  if (browser) {
    let closeTimer;
    try {
      await Promise.race([
        browser.close(),
        new Promise((_, reject) => {
          closeTimer = setTimeout(() => reject(new Error("browser_close_deadline")), 10_000);
        }),
      ]);
      receipt.browser_closed = true;
    } catch {
      receipt.status = "failed";
      receipt.failure = { stage: "browser_cleanup", reason: "close_not_confirmed" };
      process.exitCode = 1;
    } finally {
      clearTimeout(closeTimer);
    }
  }
  if (evidence) await writeFile(join(evidence, "web-real-image.json"), JSON.stringify(receipt, null, 2));
  // Never print assertion details, traces, credential input, private connection paths or HTTP bodies.
  process.stdout.write(
    JSON.stringify({
      status: receipt.status,
      stage: receipt.failure?.stage ?? "complete",
      evidence: evidence ?? null,
    }) + "\n",
  );
}

async function checkpoint(next) {
  assert.ok(!timedOut, "bounded run deadline reached before the next phase");
  stage = next;
  const progress = { status: "running", stage, completed_cases: receipt.cases.length };
  if (evidence) await writeFile(join(evidence, "progress.json"), JSON.stringify(progress, null, 2));
  process.stdout.write(JSON.stringify(progress) + "\n");
}

async function signIn(page, account, password) {
  await page.getByLabel("账号", { exact: true }).fill(account);
  await page.getByLabel("密码", { exact: true }).fill(password);
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(page.getByRole("button", { name: "退出登录", exact: true })).toBeVisible();
}
async function signOut(page) {
  await page.getByRole("button", { name: "退出登录", exact: true }).click();
  await expect(page.getByRole("heading", { name: "登录资源库", exact: true })).toBeVisible();
}
async function screenshot(page, directory, report, filename) {
  await page.screenshot({ path: join(directory, filename), animations: "disabled" });
  report.screenshots.push(filename);
}
async function completedImageResponse(responses, connection, entryId, variant) {
  const url = new URL(
    `/assetlink/v1/libraries/${connection.library_id}/entries/${entryId}/image?variant=${variant}`,
    connection.origin,
  ).href;
  for (const response of responses.filter((item) => item.url() === url).reverse())
    if ((await response.finished()) === null) return response;
  throw new Error("no_completed_real_image_response");
}
async function imageEvidence(responses, connection, entryId, variant, sample) {
  const response = await completedImageResponse(responses, connection, entryId, variant);
  assert.equal(response.status(), 200, "derived image must succeed on the real service");
  const headers = response.headers();
  assert.equal(headers["content-type"], "image/png");
  assert.ok(headers["cache-control"]?.includes("private") && headers["cache-control"]?.includes("no-store"));
  assert.equal(headers["x-content-type-options"], "nosniff");
  assert.equal(headers["cross-origin-resource-policy"], "same-origin");
  const bytes = await response.body();
  assert.equal(Number(headers["content-length"]), bytes.length);
  return { http_status: 200, ...inspectDerivedPng(bytes, variant, sample.size) };
}
