import { AssetLinkApiError } from "../assetLinkClient";
import type { PagedState } from "../types";

export function idlePage<T>(): PagedState<T> {
  return {
    status: "idle",
    items: [],
    next_cursor: null,
    message: null,
    statusCode: null,
    loadingMore: false,
  };
}

export function loadingPage<T>(): PagedState<T> {
  return { ...idlePage<T>(), status: "loading" };
}

export function failure(error: unknown): { message: string; statusCode: number | null } {
  if (error instanceof AssetLinkApiError) {
    return { message: error.message, statusCode: error.status };
  }
  return { message: "暂时无法读取资产，请稍后重试。", statusCode: null };
}

export function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError";
}
