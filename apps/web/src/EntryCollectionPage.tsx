import { useEffect, useState } from "react";
import { VirtualEntryList, type EntryRow } from "./VirtualEntryList";
import { LibraryScanStatus } from "./LibraryScanStatus";
import { EmptyState, ErrorState, LoadingState, WorkspaceLink, type Navigate } from "./WorkspacePrimitives";
import { WorkspaceIcon } from "./WorkspaceIcon";
import { CopyRelativePaths } from "./CopyRelativePaths";
import { browseRoute, type BrowseRoute, type SearchRoute } from "./workspaceRoutes";
import { parentPath } from "./libraryMetadata";
import type { Entry, Library, PagedState } from "./types";
import type { useEntrySelection } from "./hooks/useEntrySelection";
import type { useLibraryScan } from "./hooks/useLibraryScan";
import type { WorkspacePosition } from "./hooks/useWorkspaceNavigation";
import type { ImageRequests } from "./imageRequests";

export function EntryCollectionPage({
  route,
  library,
  state,
  rows,
  selection,
  scan,
  admin,
  accessFailed,
  navigate,
  loadMore,
  reload,
  reconnect,
  open,
  quickLook,
  images,
  imagesAllowed,
  position,
  remember,
}: {
  route: BrowseRoute | SearchRoute;
  library: Library | null;
  state: Omit<PagedState<Entry>, "items">;
  rows: EntryRow[];
  selection: ReturnType<typeof useEntrySelection>;
  scan: ReturnType<typeof useLibraryScan>;
  admin: boolean;
  accessFailed: boolean;
  navigate: Navigate;
  loadMore: () => void;
  reload: () => void;
  reconnect: () => void;
  open: (row: EntryRow) => void;
  quickLook: (row: EntryRow) => void;
  images: ImageRequests;
  imagesAllowed: boolean;
  position: WorkspacePosition;
  remember: (value: WorkspacePosition) => void;
}) {
  const searching = route.page === "search";
  const shortQuery = searching && route.query.trim().length < 2;
  const restoring = rows.length < position.loadedCount && state.next_cursor !== null && !accessFailed;
  useEffect(() => {
    // Restore only pages this authenticated workspace previously loaded; every page is authorized again.
    if (restoring && state.status === "ready" && !state.loadingMore) loadMore();
  }, [restoring, rows.length, state.status, state.loadingMore, loadMore]);
  const single = selection.ids.size === 1 ? rows.find((row) => selection.ids.has(row.entry.entry_id)) : undefined;
  return (
    <section className="collection-page" aria-label={searching ? "搜索结果" : "目录内容"}>
      <div className="page-heading collection-heading">
        <div>
          <span className="eyebrow">
            {searching
              ? route.scope === "all"
                ? "全部授权资源库"
                : route.scope === "library"
                  ? `${library?.display_name ?? "当前资源库"} 内搜索`
                  : "当前目录及子目录内搜索"
              : (library?.display_name ?? "资源库")}
          </span>
          <h1>
            {searching
              ? route.query.trim()
                ? `“${route.query.trim()}”`
                : "搜索资产"
              : route.path
                ? route.path.slice(route.path.lastIndexOf("/") + 1)
                : "根目录"}
          </h1>
        </div>
        <div className="content-actions">
          {!searching && route.path && (
            <WorkspaceLink
              className="secondary"
              route={browseRoute(route.libraryId, parentPath(route.path), { view: route.view })}
              navigate={navigate}
            >
              <WorkspaceIcon name="arrow-left" />
              返回上级
            </WorkspaceLink>
          )}
          <button className="secondary" onClick={reload}>
            <WorkspaceIcon name="refresh" />
            刷新
          </button>
        </div>
      </div>
      {!searching && library?.availability === "offline" && (
        <p className="storage-notice" role="status">
          此资源库暂时离线。这里保留上次成功扫描的索引，恢复连接后可刷新。
        </p>
      )}
      {!searching && admin && scan.scan?.state !== "succeeded" && (
        <LibraryScanStatus scan={scan} availability={library?.availability} />
      )}
      {!searching && (
        <div className="index-caption">
          <span>首次扫描的索引快照 · 刷新不会重新扫描目录</span>
          {admin && (
            <WorkspaceLink route={{ page: "tasks", libraryId: route.libraryId }} navigate={navigate}>
              管理扫描
              <WorkspaceIcon name="chevron" />
            </WorkspaceLink>
          )}
        </div>
      )}
      {route.page === "browse" && <BrowseFilters route={route} navigate={navigate} />}
      <div className="collection-toolbar">
        <div className="selection-summary" role="status" aria-live="polite">
          {selection.ids.size > 0 ? (
            <>
              <strong>已选择 {selection.ids.size} 项</strong>
              <button className="text-button" onClick={selection.clear}>
                取消选择
              </button>
            </>
          ) : (
            <span>
              已加载 {rows.length} 项{state.next_cursor !== null ? " · 还有更多" : ""}
            </span>
          )}
        </div>
        <div className="content-actions">
          <CopyRelativePaths rows={rows.filter((row) => selection.ids.has(row.entry.entry_id))} />
          <button className="secondary selection-open" disabled={!single} onClick={() => single && open(single)}>
            <WorkspaceIcon name={single?.entry.kind === "directory" ? "folder" : "info"} />
            {single?.entry.kind === "directory" ? "打开目录" : "查看详情"}
          </button>
          <div className="view-switch" role="group" aria-label="显示方式">
            <button
              className="icon-button"
              aria-label="列表视图"
              aria-pressed={route.view === "list"}
              onClick={() => navigate({ ...route, view: "list" }, true)}
            >
              <WorkspaceIcon name="list" />
            </button>
            <button
              className="icon-button"
              aria-label="网格视图"
              aria-pressed={route.view === "grid"}
              onClick={() => navigate({ ...route, view: "grid" }, true)}
            >
              <WorkspaceIcon name="grid" />
            </button>
          </div>
        </div>
      </div>
      {route.page === "browse" && route.anchorId && (
        <div className="anchor-notice" role="status">
          当前从定位条目开始显示
          <button className="text-button" onClick={() => navigate({ ...route, anchorId: null, entryId: null })}>
            从目录开头浏览
          </button>
        </div>
      )}
      {shortQuery && (
        <EmptyState title="搜索至少需要 2 个字符">输入文件名或相对路径关键词，并选择搜索范围。</EmptyState>
      )}
      {state.status === "loading" && <LoadingState label={searching ? "正在搜索" : "正在读取目录"} />}
      {state.status === "error" && <ErrorState message={state.message} retry={reload} />}
      {accessFailed && (
        <button className="secondary reconnect" onClick={reconnect}>
          重新连接
        </button>
      )}
      {state.status === "ready" &&
        rows.length === 0 &&
        !shortQuery &&
        !accessFailed &&
        (searching || ((!admin || scan.scan?.state === "succeeded") && library?.availability !== "offline")) && (
          <EmptyState title={searching ? "没有匹配结果" : "这个范围没有条目"}>
            {searching
              ? "试试其他文件名、路径关键词或搜索范围。"
              : "可以调整筛选条件，或在扫描任务页确认首次索引状态。"}
          </EmptyState>
        )}
      {rows.length > 0 && (
        <>
          {route.view === "list" && (
            <div className="list-columns" aria-hidden="true">
              <span>名称</span>
              <span>类型</span>
              <span>修改时间</span>
              <span>大小</span>
            </div>
          )}
          <VirtualEntryList
            rows={rows}
            view={route.view}
            selection={selection}
            position={position}
            restoring={restoring}
            remember={remember}
            onOpen={open}
            onQuickLook={quickLook}
            images={images}
            imagesAllowed={imagesAllowed}
          />
        </>
      )}
      {!accessFailed && state.next_cursor !== null && (
        <button className="load-more secondary" disabled={state.loadingMore} onClick={loadMore}>
          {state.loadingMore ? "载入中…" : "载入更多"}
        </button>
      )}
      {restoring && (
        <span className="restore-position" role="status">
          正在恢复上次浏览位置…
        </span>
      )}
      <p className="keyboard-help" id="entry-keyboard-help">
        单击选择 · Ctrl / ⌘ 多选 · Shift 连选 · 方向键移动 · Space 快速查看 · Enter 打开 · Esc 取消
      </p>
    </section>
  );
}

function BrowseFilters({ route, navigate }: { route: BrowseRoute; navigate: Navigate }) {
  const [name, setName] = useState(route.name);
  useEffect(() => setName(route.name), [route.name]);
  const change = (changes: Partial<BrowseRoute>) => navigate({ ...route, ...changes, anchorId: null, entryId: null });
  return (
    <form
      className="browse-filters"
      aria-label="目录排序和筛选"
      onSubmit={(event) => {
        event.preventDefault();
        change({ name: name.trim() });
      }}
    >
      <label className="name-filter">
        <WorkspaceIcon name="search" />
        <span className="visually-hidden">筛选当前目录名称</span>
        <input
          placeholder="筛选当前目录名称"
          value={name}
          maxLength={200}
          onChange={(event) => setName(event.target.value)}
        />
      </label>
      <button className="secondary" type="submit">
        筛选
      </button>
      <label>
        <span className="visually-hidden">条目类型</span>
        <select
          aria-label="条目类型"
          value={route.kind}
          onChange={(event) => change({ kind: event.target.value as BrowseRoute["kind"] })}
        >
          <option value="all">全部类型</option>
          <option value="files">仅文件</option>
          <option value="directories">仅目录</option>
        </select>
      </label>
      <label>
        <span className="visually-hidden">排序依据</span>
        <select
          aria-label="排序依据"
          value={route.sort}
          onChange={(event) => change({ sort: event.target.value as BrowseRoute["sort"] })}
        >
          <option value="name">按名称</option>
          <option value="modified">按修改时间</option>
          <option value="size">按大小</option>
        </select>
      </label>
      <label>
        <span className="visually-hidden">排序方向</span>
        <select
          aria-label="排序方向"
          value={route.direction}
          onChange={(event) => change({ direction: event.target.value as BrowseRoute["direction"] })}
        >
          <option value="asc">升序</option>
          <option value="desc">降序</option>
        </select>
      </label>
      {(route.name || route.kind !== "all" || route.sort !== "name" || route.direction !== "asc") && (
        <button
          className="text-button"
          type="button"
          onClick={() => change({ name: "", kind: "all", sort: "name", direction: "asc" })}
        >
          重置
        </button>
      )}
    </form>
  );
}
