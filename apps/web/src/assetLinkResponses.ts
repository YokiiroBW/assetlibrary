import { isLibraryCategory } from "./libraryMetadata";
import type { Entry, EntryDetail, EntryPage, Library, Page, SearchHit } from "./types";
const pageSize = 100;
export function decodeLibraryPage(value: Record<string, unknown>): Page<Library> {
  return {
    items: pageItems(value.items, "items").map(decodeLibrary),
    next_cursor: optionalString(value.next_cursor, "next_cursor"),
  };
}

export function decodeEntryPage(value: Record<string, unknown>): EntryPage {
  return {
    library: decodeLibrary(value.library),
    parent_relative_path: string(value.parent_relative_path, "parent_relative_path"),
    items: pageItems(value.items, "items").map(decodeEntry),
    next_cursor: optionalString(value.next_cursor, "next_cursor"),
  };
}

export function decodeSearchPage(value: Record<string, unknown>): Page<SearchHit> {
  return {
    items: pageItems(value.items, "items").map((item) => {
      const hit = record(item, "search hit");
      const reason = string(hit.hit_reason, "hit_reason");
      if (reason !== "name" && reason !== "path") {
        throw new TypeError("hit_reason is invalid");
      }
      const library = decodeLibrary(hit.library);
      const entry = decodeEntry(hit.entry);
      if (entry.library_id !== library.library_id) throw new TypeError("The search entry does not match its library");
      return {
        library,
        entry,
        hit_reason: reason,
      };
    }),
    next_cursor: optionalString(value.next_cursor, "next_cursor"),
  };
}

export function decodeLibrary(value: unknown): Library {
  const item = record(value, "library");
  const availability = string(item.availability, "availability");
  const accessLevel = string(item.access_level, "access_level");
  if (availability !== "online" && availability !== "offline") {
    throw new TypeError("availability is invalid");
  }
  if (!isAccessLevel(accessLevel)) {
    throw new TypeError("access_level is invalid");
  }
  const category = item.category === undefined ? "general" : string(item.category, "category");
  if (!isLibraryCategory(category)) throw new TypeError("category is invalid");
  return {
    library_id: string(item.library_id, "library_id"),
    display_name: string(item.display_name, "display_name"),
    availability,
    access_level: accessLevel,
    category,
  };
}

export function decodeEntry(value: unknown): Entry {
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

export function decodeEntryDetail(value: Record<string, unknown>, libraryId: string, entryId: string): EntryDetail {
  const library = decodeLibrary(value.library);
  const entry = decodeEntry(value.entry);
  if (library.library_id !== libraryId || entry.library_id !== libraryId || entry.entry_id !== entryId)
    throw new TypeError("The entry detail does not match its request");
  return { library, entry };
}

export function record(value: unknown, name: string): Record<string, unknown> {
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

export function string(value: unknown, name: string): string {
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
