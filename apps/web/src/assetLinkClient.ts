import { encodeAssetLinkMessage, parseAssetLinkMessage, type ControlRequest } from "@assetlibrary/assetlink";
import type { Entry, EntryPage, Library, Page, SearchHit } from "./types";

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
  public listLibraries(cursor: string | null, signal: AbortSignal): Promise<Page<Library>> {
    return this.request("libraries.list", pageBody(cursor), signal, decodeLibraryPage);
  }

  public browseEntries(
    libraryId: string,
    parentRelativePath: string,
    cursor: string | null,
    signal: AbortSignal,
  ): Promise<EntryPage> {
    return this.request(
      "entries.browse",
      {
        library_id: libraryId,
        parent_relative_path: parentRelativePath,
        ...pageBody(cursor),
      },
      signal,
      decodeEntryPage,
    );
  }

  public searchAssets(query: string, cursor: string | null, signal: AbortSignal): Promise<Page<SearchHit>> {
    return this.request("assets.search", { query, ...pageBody(cursor) }, signal, decodeSearchPage);
  }

  private async request<T>(
    operation: string,
    body: Record<string, unknown>,
    signal: AbortSignal,
    decode: (body: Record<string, unknown>) => T,
  ): Promise<T> {
    const request: ControlRequest = {
      message_type: "control.request",
      request_id: crypto.randomUUID(),
      operation,
      body,
      timeout_ms: timeoutMilliseconds,
    };
    const response = await fetch(endpoint, {
      method: "POST",
      credentials: "same-origin",
      headers: { "content-type": "application/json" },
      body: encodeAssetLinkMessage(request),
      signal,
    });
    const message = parseAssetLinkMessage(await response.text());
    if (message.request_id !== request.request_id) {
      throw new AssetLinkApiError(response.status, "invalid_response", "服务返回了无法识别的响应。");
    }
    if (message.message_type === "error") {
      const error = record(message.error, "error");
      throw new AssetLinkApiError(
        response.status,
        string(error.code, "error.code"),
        string(error.message, "error.message"),
      );
    }
    if (!response.ok || message.message_type !== "control.result" || !message.ok) {
      throw new AssetLinkApiError(response.status, "invalid_response", "服务返回了无法识别的响应。");
    }
    return decode(record(message.body, "body"));
  }
}

function pageBody(cursor: string | null): Record<string, unknown> {
  return cursor === null ? { page_size: pageSize } : { page_size: pageSize, cursor };
}

function decodeLibraryPage(value: Record<string, unknown>): Page<Library> {
  return {
    items: pageItems(value.items, "items").map(decodeLibrary),
    next_cursor: optionalString(value.next_cursor, "next_cursor"),
  };
}

function decodeEntryPage(value: Record<string, unknown>): EntryPage {
  return {
    library: decodeLibrary(value.library),
    parent_relative_path: string(value.parent_relative_path, "parent_relative_path"),
    items: pageItems(value.items, "items").map(decodeEntry),
    next_cursor: optionalString(value.next_cursor, "next_cursor"),
  };
}

function decodeSearchPage(value: Record<string, unknown>): Page<SearchHit> {
  return {
    items: pageItems(value.items, "items").map((item) => {
      const hit = record(item, "search hit");
      const reason = string(hit.hit_reason, "hit_reason");
      if (reason !== "name" && reason !== "path") {
        throw new TypeError("hit_reason is invalid");
      }
      return {
        library: decodeLibrary(hit.library),
        entry: decodeEntry(hit.entry),
        hit_reason: reason,
      };
    }),
    next_cursor: optionalString(value.next_cursor, "next_cursor"),
  };
}

function decodeLibrary(value: unknown): Library {
  const item = record(value, "library");
  const availability = string(item.availability, "availability");
  const accessLevel = string(item.access_level, "access_level");
  if (availability !== "online" && availability !== "offline") {
    throw new TypeError("availability is invalid");
  }
  if (!isAccessLevel(accessLevel)) {
    throw new TypeError("access_level is invalid");
  }
  return {
    library_id: string(item.library_id, "library_id"),
    display_name: string(item.display_name, "display_name"),
    availability,
    access_level: accessLevel,
  };
}

function decodeEntry(value: unknown): Entry {
  const item = record(value, "entry");
  const kind = string(item.kind, "kind");
  if (!isEntryKind(kind)) {
    throw new TypeError("kind is invalid");
  }
  return {
    entry_id: string(item.entry_id, "entry_id"),
    library_id: string(item.library_id, "library_id"),
    relative_path: string(item.relative_path, "relative_path"),
    name: string(item.name, "name"),
    kind,
    content_length: optionalString(item.content_length, "content_length"),
    last_write_time_utc: string(item.last_write_time_utc, "last_write_time_utc"),
  };
}

function record(value: unknown, name: string): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new TypeError(`${name} must be an object`);
  }
  return value as Record<string, unknown>;
}

function array(value: unknown, name: string): unknown[] {
  if (!Array.isArray(value)) {
    throw new TypeError(`${name} must be an array`);
  }
  return value;
}

function pageItems(value: unknown, name: string): unknown[] {
  const items = array(value, name);
  if (items.length > pageSize) {
    throw new TypeError(`${name} exceeds the page bound`);
  }
  return items;
}

function string(value: unknown, name: string): string {
  if (typeof value !== "string") {
    throw new TypeError(`${name} must be a string`);
  }
  return value;
}

function optionalString(value: unknown, name: string): string | null {
  return value === null || value === undefined ? null : string(value, name);
}

function isAccessLevel(value: string): value is Library["access_level"] {
  return ["read_only", "read_write", "organize", "library_administrator"].includes(value);
}

function isEntryKind(value: string): value is Entry["kind"] {
  return ["file", "directory", "reparse_file", "reparse_directory"].includes(value);
}
