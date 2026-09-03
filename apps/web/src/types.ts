export type Availability = "online" | "offline";
export type AccessLevel = "read_only" | "read_write" | "organize" | "library_administrator";
export type EntryKind = "file" | "directory" | "reparse_file" | "reparse_directory";
export type SearchHitReason = "name" | "path";

export interface Library {
  library_id: string;
  display_name: string;
  availability: Availability;
  access_level: AccessLevel;
}

export interface Entry {
  entry_id: string;
  library_id: string;
  relative_path: string;
  name: string;
  kind: EntryKind;
  content_length: string | null;
  last_write_time_utc: string;
}

export interface SearchHit {
  library: Library;
  entry: Entry;
  hit_reason: SearchHitReason;
}

export interface Page<T> {
  items: T[];
  next_cursor: string | null;
}

export interface EntryPage extends Page<Entry> {
  library: Library;
  parent_relative_path: string;
}

export type LoadStatus = "idle" | "loading" | "ready" | "error";

export interface PagedState<T> extends Page<T> {
  status: LoadStatus;
  message: string | null;
  statusCode: number | null;
  loadingMore: boolean;
}
