import {
  browserSession,
  browsePage,
  entry,
  entryDetail,
  failure,
  libraryPage,
  libraryDetail,
  mockAssetLink,
  mockSession,
  result,
  scanSummary,
  searchPage,
  visibleLibrary,
} from "./assetlink-fixtures.mjs";

export async function mockTrial(page, { empty = false } = {}) {
  const state = {
    library: empty ? null : { ...visibleLibrary, access_level: "library_administrator" },
    scan: empty
      ? null
      : scanSummary({ state: "succeeded", can_cancel: false, committed_entries: 1, observed_entries: 1 }),
    requests: [],
    registerFailure: null,
    startFailure: null,
    cancelFailure: null,
    scanFailure: null,
    sources: [{ source_key: "fixtures", display_name: "试用存储", default_root_path: "C:/fixture-storage" }],
    tasksStarted: 0,
    browseCursor: null,
    categoryFailure: null,
  };
  await mockSession(page, { ...browserSession, is_system_administrator: true, display_name: "管理员" });
  await mockAssetLink(page, (request) => {
    state.requests.push(request);
    if (request.operation === "libraries.list")
      return libraryPage(request, state.library === null || (request.body.category && request.body.category !== state.library.category) ? [] : [state.library]);
    if (request.operation === "libraries.get") return state.library ? libraryDetail(request, state.library) : failure(request, 404, "not_found", "资源库不可用。");
    if (request.operation === "entries.get") return entryDetail(request, entry(980, { name: "sample-photo.jpg", relative_path: "sample-photo.jpg" }), state.library);
    if (request.operation === "entries.browse")
      return browsePage(
        request,
        state.scan?.state === "succeeded"
          ? [entry(980, { name: "sample-photo.jpg", relative_path: "sample-photo.jpg" })]
          : [],
        state.browseCursor,
        state.library ?? visibleLibrary,
      );
    if (request.operation === "assets.search") return searchPage(request, []);
    if (request.operation === "storage_sources.list") return result(request, { sources: state.sources });
    if (request.operation === "libraries.register") {
      if (state.registerFailure !== null)
        return failure(
          request,
          state.registerFailure,
          "registration_rejected",
          "目录不可用或与已有资源库重叠，请检查后重试。",
        );
      state.library = {
        ...visibleLibrary,
        display_name: request.body.display_name,
        category: request.body.category ?? "general",
        access_level: "library_administrator",
      };
      state.scan = null;
      return result(request, { library_id: state.library.library_id });
    }
    if (request.operation === "libraries.update_category") {
      if (state.categoryFailure || request.body.expected_category !== state.library.category)
        return failure(request, state.categoryFailure ?? 409, "state_conflict", "资源库分类已改变。");
      state.library = { ...state.library, category: request.body.category };
      return result(request, { library_id: state.library.library_id, category: state.library.category });
    }
    if (request.operation === "library_scans.get" && state.scanFailure !== null)
      return failure(request, state.scanFailure, "service_unavailable", "暂时无法读取扫描状态。");
    if (request.operation === "library_scans.start") {
      if (state.startFailure !== null)
        return failure(request, state.startFailure, "service_unavailable", "扫描请求尚未确认，请重试。");
      state.tasksStarted++;
      state.scan = scanSummary({ task_id: `77777777-7777-4777-8777-${String(state.tasksStarted).padStart(12, "0")}` });
    }
    if (request.operation === "library_scans.cancel") {
      if (state.cancelFailure !== null)
        return failure(request, state.cancelFailure, "service_unavailable", "取消请求尚未确认，请重试。");
      state.scan = { ...state.scan, cancellation_requested: true };
    }
    if (request.operation.startsWith("library_scans."))
      return result(request, { library_id: request.body.library_id, scan: state.scan });
    throw new Error(`Unexpected trial operation: ${request.operation}`);
  });
  return state;
}
