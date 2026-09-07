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

export class AssetLinkApiError extends Error {
  public constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = "AssetLinkApiError";
  }
}

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
    return this.transport(endpoint, "POST", encodeAssetLinkMessage(request), signal, (text, response) => {
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
      (text, response) => {
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
    decode: (text: string, response: Response) => T,
  ): Promise<T> {
    signal.throwIfAborted();
    const controller = new AbortController();
    const cancelFromCaller = () => controller.abort(signal.reason);
    signal.addEventListener("abort", cancelFromCaller, { once: true });
    const timeout = window.setTimeout(() => controller.abort(), timeoutMilliseconds);
    try {
      const response = await fetch(path, {
        method,
        credentials: "same-origin",
        cache: "no-store",
        headers: { "content-type": "application/json", "X-AssetLibrary-CSRF": this.csrfToken },
        body,
        signal: controller.signal,
      });
      try {
        return decode(await response.text(), response);
      } catch (error: unknown) {
        // HTTP rejection remains authoritative even when its body stalls or is malformed.
        if (!response.ok && !(error instanceof AssetLinkApiError)) throw invalidResponse(response.status);
        throw error;
      }
    } catch (error: unknown) {
      if (signal.aborted) throw signal.reason;
      if (error instanceof AssetLinkApiError) throw error;
      if (controller.signal.aborted) throw new AssetLinkApiError(504, "timeout", "读取超时，请重试。");
      throw error;
    } finally {
      window.clearTimeout(timeout);
      signal.removeEventListener("abort", cancelFromCaller);
    }
  }
}

function pageBody(cursor: string | null): Record<string, unknown> {
  return cursor === null ? { page_size: pageSize } : { page_size: pageSize, cursor };
}

function invalidResponse(status: number): AssetLinkApiError {
  return new AssetLinkApiError(status, "invalid_response", "服务返回了无法识别的响应。");
}
