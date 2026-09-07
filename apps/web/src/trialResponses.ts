import { record, string } from "./assetLinkResponses";
import type { BrowserSession, LibraryScan, ScanState, StorageSource } from "./types";

export function decodeSession(body: Record<string, unknown>): BrowserSession {
  if (body.authenticated !== true) throw new TypeError("An authenticated session is required");
  return {
    authenticated: true,
    principal_id: nonempty(body.principal_id, "principal_id"),
    display_name: nonempty(body.display_name, "display_name"),
    is_system_administrator: boolean(body.is_system_administrator, "is_system_administrator"),
    csrf_token: nonempty(body.csrf_token, "csrf_token"),
    absolute_expires_at: timestamp(body.absolute_expires_at, "absolute_expires_at"),
  };
}

export function decodeSources(body: Record<string, unknown>): StorageSource[] {
  if (!Array.isArray(body.sources) || body.sources.length > 32)
    throw new TypeError("The storage source list is invalid");
  return body.sources.map((value: unknown) => {
    const item = record(value, "storage source");
    return {
      source_key: nonempty(item.source_key, "source_key"),
      display_name: nonempty(item.display_name, "display_name"),
      default_root_path:
        item.default_root_path === undefined ? null : nonempty(item.default_root_path, "default_root_path"),
    };
  });
}

export function decodeScan(body: Record<string, unknown>, libraryId: string, requireScan = false): LibraryScan {
  if (body.library_id !== libraryId) throw new TypeError("The scan library does not match");
  if (body.scan === null && !requireScan) return { library_id: libraryId, scan: null };
  const value = record(body.scan, "scan");
  const state = string(value.state, "state");
  if (!isScanState(state)) throw new TypeError("The scan state is invalid");
  return {
    library_id: libraryId,
    scan: {
      task_id: nonempty(value.task_id, "task_id"),
      scan_id: nullableString(value.scan_id, "scan_id"),
      state,
      cancellation_requested: boolean(value.cancellation_requested, "cancellation_requested"),
      observed_entries: count(value.observed_entries, "observed_entries"),
      committed_entries: count(value.committed_entries, "committed_entries"),
      started_at: value.started_at === null ? null : timestamp(value.started_at, "started_at"),
      finished_at: value.finished_at === null ? null : timestamp(value.finished_at, "finished_at"),
      failure_code: nullableString(value.failure_code, "failure_code"),
      can_cancel: boolean(value.can_cancel, "can_cancel"),
      can_retry: boolean(value.can_retry, "can_retry"),
    },
  };
}

function isScanState(value: string): value is ScanState {
  return (
    value === "queued" || value === "leased" || value === "succeeded" || value === "failed" || value === "cancelled"
  );
}

function boolean(value: unknown, name: string): boolean {
  if (typeof value !== "boolean") throw new TypeError(`${name} is invalid`);
  return value;
}

function count(value: unknown, name: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < 0) throw new TypeError(`${name} is invalid`);
  return value;
}

function nonempty(value: unknown, name: string): string {
  const result = string(value, name);
  if (result.length === 0) throw new TypeError(`${name} is required`);
  return result;
}

function nullableString(value: unknown, name: string): string | null {
  return value === null ? null : nonempty(value, name);
}

function timestamp(value: unknown, name: string): string {
  const result = nonempty(value, name);
  if (!Number.isFinite(Date.parse(result))) throw new TypeError(`${name} is invalid`);
  return result;
}
