import { encodeAssetLinkMessage, parseAssetLinkMessage, type ControlRequest } from "@assetlibrary/assetlink";
import {
  decodeEntryDetail,
  decodeEntryPage,
  decodeLibrary,
  decodeLibraryPage,
  decodeSearchPage,
  record,
  string,
} from "./assetLinkResponses";
import { decodeScan, decodeSession, decodeSources } from "./trialResponses";
import { AssetLinkApiError } from "./assetLinkError";
import { imageFailure, readImageResponse, type DerivedImage, type ImageVariant } from "./imageResponses";
export { AssetLinkApiError } from "./assetLinkError";
import type {
  BrowserSession,
  BrowseOptions,
  EntryDetail,
  EntryPage,
  Library,
  LibraryCategory,
  LibraryScan,
  Page,
  RegisterLibraryRequest,
  SearchHit,
  SearchOptions,
  StorageSource,
} from "./types";

const endpoint = "/assetlink/v1/control";
const pageSize = 100;
const timeoutMilliseconds = 5_000;
/** The workbench's report reads answer a bounded page, so they fit inside the same request budget. */
const dedupTimeoutMilliseconds = 15_000;

export class AssetLinkClient {
  public constructor(private readonly csrfToken = "") {}

  public getSession(signal: AbortSignal): Promise<BrowserSession> {
    return this.auth("session", "GET", undefined, signal, decodeSession);
  }

  public signIn(accountName: string, password: string, signal: AbortSignal): Promise<BrowserSession> {
    return this.auth("login", "POST", { account_name: accountName, password }, signal, decodeSession);
  }

  public signOut(signal: AbortSignal): Promise<void> {
    return this.auth("logout", "POST", {}, signal, (_body, status) => {
      if (status !== 204) throw new TypeError("A logout acknowledgement is required");
    });
  }

  public listLibraries(cursor: string | null, signal: AbortSignal, category?: LibraryCategory): Promise<Page<Library>> {
    return this.request(
      "libraries.list",
      { ...pageBody(cursor), ...(category === undefined ? {} : { category }) },
      signal,
      decodeLibraryPage,
    );
  }

  public getLibrary(libraryId: string, signal: AbortSignal): Promise<Library> {
    return this.request("libraries.get", { library_id: libraryId }, signal, (body) => {
      const library = decodeLibrary(body.library);
      if (library.library_id !== libraryId) throw new TypeError("The library does not match its request");
      return library;
    });
  }

  public getEntry(libraryId: string, entryId: string, signal: AbortSignal): Promise<EntryDetail> {
    return this.request("entries.get", { library_id: libraryId, entry_id: entryId }, signal, (body) =>
      decodeEntryDetail(body, libraryId, entryId),
    );
  }

  public getImage(
    libraryId: string,
    entryId: string,
    variant: ImageVariant,
    signal: AbortSignal,
  ): Promise<DerivedImage> {
    const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (
      ![libraryId, entryId].every((id) => uuid.test(id) && id !== "00000000-0000-0000-0000-000000000000") ||
      (variant !== "thumbnail" && variant !== "preview")
    )
      return Promise.reject(imageFailure(400, "invalid_request"));
    return this.transport(
      `/assetlink/v1/libraries/${libraryId}/entries/${entryId}/image?variant=${variant}`,
      "GET",
      undefined,
      signal,
      (response, transportSignal) => readImageResponse(response, transportSignal, variant),
      true,
    );
  }

  public updateCategory(
    libraryId: string,
    category: LibraryCategory,
    expectedCategory: LibraryCategory,
    idempotencyKey: string,
    signal: AbortSignal,
  ): Promise<void> {
    return this.request(
      "libraries.update_category",
      { library_id: libraryId, category, expected_category: expectedCategory },
      signal,
      (body) => {
        if (body.library_id !== libraryId || body.category !== category)
          throw new TypeError("The category update was not confirmed");
      },
      idempotencyKey,
    );
  }

  public browseEntries(
    libraryId: string,
    parentRelativePath: string,
    cursor: string | null,
    signal: AbortSignal,
    options?: BrowseOptions,
  ): Promise<EntryPage> {
    return this.request(
      "entries.browse",
      {
        library_id: libraryId,
        parent_relative_path: parentRelativePath,
        ...pageBody(cursor),
        ...options,
        ...(cursor !== null ? { anchor_entry_id: undefined } : {}),
      },
      signal,
      (body) => {
        const page = decodeEntryPage(body);
        if (
          page.library.library_id !== libraryId ||
          page.parent_relative_path !== parentRelativePath ||
          page.items.some((entry) => entry.library_id !== libraryId)
        )
          throw new TypeError("The directory does not match its request");
        return page;
      },
    );
  }

  public searchAssets(
    query: string,
    cursor: string | null,
    signal: AbortSignal,
    options?: SearchOptions,
  ): Promise<Page<SearchHit>> {
    return this.request("assets.search", { query, ...pageBody(cursor), ...options }, signal, decodeSearchPage);
  }

  public listStorageSources(signal: AbortSignal): Promise<StorageSource[]> {
    return this.request("storage_sources.list", {}, signal, decodeSources);
  }

  public registerLibrary(body: RegisterLibraryRequest, idempotencyKey: string, signal: AbortSignal): Promise<string> {
    return this.request(
      "libraries.register",
      { ...body },
      signal,
      (result) => string(result.library_id, "library_id"),
      idempotencyKey,
    );
  }

  public getLibraryScan(libraryId: string, signal: AbortSignal): Promise<LibraryScan> {
    return this.request("library_scans.get", { library_id: libraryId }, signal, (body) => decodeScan(body, libraryId));
  }

  public startLibraryScan(libraryId: string, idempotencyKey: string, signal: AbortSignal): Promise<LibraryScan> {
    return this.request(
      "library_scans.start",
      { library_id: libraryId },
      signal,
      (body) => decodeScan(body, libraryId, true),
      idempotencyKey,
    );
  }

  public cancelLibraryScan(
    libraryId: string,
    taskId: string,
    idempotencyKey: string,
    signal: AbortSignal,
  ): Promise<LibraryScan> {
    return this.request(
      "library_scans.cancel",
      { library_id: libraryId, task_id: taskId },
      signal,
      (body) => decodeScan(body, libraryId, true),
      idempotencyKey,
    );
  }

  /**
   * The exact-duplicate workbench conversation. It is not part of the control envelope: the workbench
   * has its own bounded operation paths, which this adapter is the single place allowed to reach. Only a
   * library identifier is ever sent, so the server always reads the root it resolved itself.
   */
  public dedup(
    operation: string,
    body: Record<string, unknown>,
    signal: AbortSignal,
    decode: (value: unknown) => unknown,
  ): Promise<unknown> {
    return this.transport(
      `/assetlink/v1/dedup/${operation}`,
      "POST",
      JSON.stringify(body),
      signal,
      async (response) => {
        const text = await response.text();
        const payload: unknown = text.length === 0 ? null : JSON.parse(text);
        if (!response.ok) throw dedupFailure(payload, response.status);
        return decode(payload);
      },
      false,
      dedupTimeoutMilliseconds,
    );
  }

  private request<T>(
    operation: string,
    body: Record<string, unknown>,
    signal: AbortSignal,
    decode: (body: Record<string, unknown>) => T,
    idempotencyKey?: string,
  ): Promise<T> {
    signal.throwIfAborted();
    const request: ControlRequest = {
      message_type: "control.request",
      request_id: idempotencyKey ?? crypto.randomUUID(),
      operation,
      body,
      timeout_ms: timeoutMilliseconds,
      ...(idempotencyKey === undefined ? {} : { idempotency_key: idempotencyKey }),
    };
    return this.transport(endpoint, "POST", encodeAssetLinkMessage(request), signal, async (response) => {
      const text = await response.text();
      const message = parseAssetLinkMessage(text);
      if (message.request_id !== request.request_id) throw invalidResponse(response.status);
      if (message.message_type === "error") {
        const error = record(message.error, "error");
        throw new AssetLinkApiError(
          response.status,
          string(error.code, "error.code"),
          string(error.message, "error.message"),
        );
      }
      if (!response.ok || message.message_type !== "control.result" || !message.ok)
        throw invalidResponse(response.status);
      return decode(record(message.body, "body"));
    });
  }

  private auth<T>(
    operation: "session" | "login" | "logout",
    method: "GET" | "POST",
    body: Record<string, unknown> | undefined,
    signal: AbortSignal,
    decode: (body: Record<string, unknown>, status: number) => T,
  ): Promise<T> {
    return this.transport(
      `/assetlink/v1/auth/${operation}`,
      method,
      body === undefined ? undefined : JSON.stringify(body),
      signal,
      async (response) => {
        const text = await response.text();
        const result = response.status === 204 ? {} : record(JSON.parse(text), "session response");
        if (!response.ok)
          throw new AssetLinkApiError(response.status, string(result.code, "code"), string(result.message, "message"));
        return decode(result, response.status);
      },
    );
  }

  private async transport<T>(
    path: string,
    method: "GET" | "POST",
    body: string | undefined,
    signal: AbortSignal,
    decode: (response: Response, signal: AbortSignal) => Promise<T>,
    imageRequest = false,
    budgetMilliseconds = timeoutMilliseconds,
  ): Promise<T> {
    signal.throwIfAborted();
    const controller = new AbortController();
    const cancelFromCaller = () => controller.abort(signal.reason);
    signal.addEventListener("abort", cancelFromCaller, { once: true });
    const timeout = window.setTimeout(() => controller.abort(), imageRequest ? 20_000 : budgetMilliseconds);
    try {
      for (let attempt = 0; ; attempt++) {
        const response = await fetch(path, {
          method,
          credentials: "same-origin",
          cache: "no-store",
          redirect: "error",
          headers: { "content-type": "application/json", "X-AssetLibrary-CSRF": this.csrfToken },
          body,
          signal: controller.signal,
        });
        if (imageRequest && [401, 403, 404].includes(response.status)) {
          void response.body?.cancel().catch(() => undefined);
          throw imageFailure(response.status);
        }
        if (imageRequest && response.status === 429 && attempt < 2) {
          void response.body?.cancel().catch(() => undefined);
          await waitForRetry(response.headers.get("retry-after"), controller.signal);
          continue;
        }
        try {
          return await decode(response, controller.signal);
        } catch (error: unknown) {
          // HTTP rejection remains authoritative even when its body stalls or is malformed.
          if (!response.ok && !(error instanceof AssetLinkApiError)) throw invalidResponse(response.status);
          throw error;
        }
      }
    } catch (error: unknown) {
      if (signal.aborted) throw signal.reason;
      if (error instanceof AssetLinkApiError) throw error;
      if (controller.signal.aborted)
        throw imageRequest
          ? imageFailure(504, "preview_timeout")
          : new AssetLinkApiError(504, "timeout", "读取超时，请重试。");
      throw error;
    } finally {
      window.clearTimeout(timeout);
      controller.abort();
      signal.removeEventListener("abort", cancelFromCaller);
    }
  }
}

function waitForRetry(value: string | null, signal: AbortSignal): Promise<void> {
  const seconds = value !== null && /^\d+$/.test(value) ? Number(value) * 1000 : Date.parse(value ?? "") - Date.now();
  const delay = Number.isFinite(seconds) ? Math.max(0, Math.min(seconds, 20_000)) : 1000;
  return new Promise((resolve, reject) => {
    signal.throwIfAborted();
    const cancel = () => {
      window.clearTimeout(timer);
      reject(signal.reason);
    };
    const timer = window.setTimeout(() => {
      signal.removeEventListener("abort", cancel);
      resolve();
    }, delay);
    signal.addEventListener("abort", cancel, { once: true });
  });
}

function pageBody(cursor: string | null): Record<string, unknown> {
  return cursor === null ? { page_size: pageSize } : { page_size: pageSize, cursor };
}

function invalidResponse(status: number): AssetLinkApiError {
  return new AssetLinkApiError(status, "invalid_response", "服务返回了无法识别的响应。");
}

/**
 * Turns a workbench error body into a typed failure. The server's own message is kept when it sent one,
 * because "结果游标已过期" and "没有权限" call for different actions from the reader.
 */
function dedupFailure(payload: unknown, status: number): AssetLinkApiError {
  if (payload !== null && typeof payload === "object" && "error" in payload) {
    const error = (payload as { error?: unknown }).error;
    if (error !== null && typeof error === "object") {
      const code = (error as { code?: unknown }).code;
      const message = (error as { message?: unknown }).message;
      if (typeof code === "string" && typeof message === "string") {
        return new AssetLinkApiError(status, code, message);
      }
    }
  }

  return invalidResponse(status);
}
