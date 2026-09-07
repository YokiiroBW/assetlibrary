export type Availability = "online" | "offline";
export type AccessLevel = "read_only" | "read_write" | "organize" | "library_administrator";
export type EntryKind = "file" | "directory" | "reparse_file" | "reparse_directory";
export type SearchHitReason = "name" | "path";
export type LibraryCategory =
  "photos" | "images" | "videos" | "music" | "projects" | "documents" | "characters" | "general";
export type EntryView = "list" | "grid";
export type EntrySort = "name" | "modified" | "size";
export type SortDirection = "asc" | "desc";
export type EntryFilterKind = "all" | "files" | "directories";
export interface BrowseOptions {
  sort_by: EntrySort;
  sort_direction: SortDirection;
  kind: EntryFilterKind;
  name_filter: string;
  anchor_entry_id?: string;
}
export interface SearchOptions {
  scope: "all" | "library" | "directory";
  library_id?: string;
  parent_relative_path?: string;
}

export interface Library {
  library_id: string;
  display_name: string;
  availability: Availability;
  access_level: AccessLevel;
  category: LibraryCategory;
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

export interface EntryDetail {
  library: Library;
  entry: Entry;
}

export type LoadStatus = "idle" | "loading" | "ready" | "error";

export interface PagedState<T> extends Page<T> {
  status: LoadStatus;
  message: string | null;
  statusCode: number | null;
  loadingMore: boolean;
}

export interface BrowserSession {
  authenticated: true;
  principal_id: string;
  display_name: string;
  is_system_administrator: boolean;
  csrf_token: string;
  absolute_expires_at: string;
}

export interface StorageSource {
  source_key: string;
  display_name: string;
  default_root_path: string | null;
}

export interface RegisterLibraryRequest {
  source_key: string;
  display_name: string;
  root_path: string;
  category?: LibraryCategory;
}

export type ScanState = "queued" | "leased" | "succeeded" | "failed" | "cancelled";

export interface ScanSummary {
  task_id: string;
  scan_id: string | null;
  state: ScanState;
  cancellation_requested: boolean;
  observed_entries: number;
  committed_entries: number;
  started_at: string | null;
  finished_at: string | null;
  failure_code: string | null;
  can_cancel: boolean;
  can_retry: boolean;
}

export interface LibraryScan {
  library_id: string;
  scan: ScanSummary | null;
}
