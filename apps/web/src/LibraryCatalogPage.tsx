import { useRef } from "react";
import { useVirtualizer } from "./hooks/useRowWindow";
import { accessLabel, categoryLabels, libraryCategories } from "./libraryMetadata";
import { browseRoute } from "./workspaceRoutes";
import { EmptyState, ErrorState, LoadingState, WorkspaceLink, type Navigate } from "./WorkspacePrimitives";
import { WorkspaceIcon } from "./WorkspaceIcon";
import type { Library, LibraryCategory } from "./types";
import type { useLibraries } from "./hooks/useLibraries";

export function LibraryCatalogPage({
  home,
  category,
  libraries,
  admin,
  navigate,
  register,
  editCategory,
}: {
  home: boolean;
  category: LibraryCategory | null;
  libraries: ReturnType<typeof useLibraries>;
  admin: boolean;
  navigate: Navigate;
  register: () => void;
  editCategory: (library: Library) => void;
}) {
  const { state } = libraries;
  return (
    <section className="catalog-page">
      <div className="page-heading">
        <div>
          <span className="eyebrow">{home ? "工作区" : category ? "分类聚合视图" : "库管理"}</span>
          <h1>{home ? "首页" : category ? `${categoryLabels[category]}资源库` : "资源库管理"}</h1>
          <p>{home ? "从资源库进入真实目录，浏览已提交的索引。" : "按明确分类组织资源库，物理目录保持原有归属。"}</p>
        </div>
        {admin && (
          <button className="primary" onClick={register}>
            <WorkspaceIcon name="plus" />
            添加资源库
          </button>
        )}
      </div>
      {home && (
        <nav className="category-overview" aria-label="分类入口">
          {libraryCategories.map((item) => (
            <WorkspaceLink key={item} route={{ page: "libraries", category: item }} navigate={navigate}>
              <WorkspaceIcon name={item} className={`category-${item}`} />
              <span>{categoryLabels[item]}</span>
            </WorkspaceLink>
          ))}
        </nav>
      )}
      <div className="section-heading">
        <div>
          <h2>{home ? "资源库概览" : "资源库列表"}</h2>
          <p>
            已加载 {state.items.length} 个授权资源库{state.next_cursor !== null ? " · 还有更多可载入" : ""}
          </p>
        </div>
        <button className="secondary" onClick={libraries.reload}>
          <WorkspaceIcon name="refresh" />
          刷新
        </button>
      </div>
      {state.status === "loading" && <LoadingState label="正在读取资源库" />}
      {state.status === "error" && <ErrorState message={state.message} retry={libraries.reload} />}
      {state.status === "ready" && state.items.length === 0 && (
        <EmptyState title={category ? "此分类暂无资源库" : "没有可见资源库"}>
          {admin ? "添加已配置范围内的目录，然后单独启动首次扫描。" : "当前账号尚未获得此范围内资源库的读取权限。"}
        </EmptyState>
      )}
      {state.items.length > 0 && (
        <LibraryTable items={state.items} admin={admin} navigate={navigate} editCategory={editCategory} />
      )}
      {state.next_cursor !== null && (
        <button className="secondary load-more" disabled={state.loadingMore} onClick={libraries.loadMore}>
          {state.loadingMore ? "载入中…" : "载入更多资源库"}
        </button>
      )}
      {home && admin && (
        <div className="home-task-entry">
          <div>
            <WorkspaceIcon name="tasks" />
            <span>
              <strong>管理首次扫描</strong>
              <small>选择一个资源库，查看真实进度、取消或重试未完成任务。</small>
            </span>
          </div>
          <WorkspaceLink className="secondary" route={{ page: "tasks", libraryId: null }} navigate={navigate}>
            打开扫描任务
            <WorkspaceIcon name="chevron" />
          </WorkspaceLink>
        </div>
      )}
    </section>
  );
}

function LibraryTable({
  items,
  admin,
  navigate,
  editCategory,
}: {
  items: Library[];
  admin: boolean;
  navigate: Navigate;
  editCategory: (library: Library) => void;
}) {
  const scroll = useRef<HTMLDivElement>(null);
  const virtual = useVirtualizer({
    count: items.length,
    getScrollElement: () => scroll.current,
    estimateSize: () => 72,
    overscan: 5,
  });
  return (
    <>
      <div className="library-table-head" aria-hidden="true">
        <span>资源库</span>
        <span>分类</span>
        <span>状态 / 权限</span>
        <span>管理</span>
      </div>
      <div ref={scroll} className="catalog-scroll" role="list" aria-label="资源库列表">
        <div style={{ height: virtual.getTotalSize(), position: "relative" }}>
          {virtual.getVirtualItems().map((row) => {
            const library = items[row.index];
            if (!library) return null;
            return (
              <div
                key={library.library_id}
                className="library-table-row"
                role="listitem"
                style={{ transform: `translateY(${row.start}px)` }}
              >
                <WorkspaceLink
                  className="library-title-link"
                  route={browseRoute(library.library_id)}
                  navigate={navigate}
                >
                  <span className={`library-icon category-${library.category}`}>
                    <WorkspaceIcon name={library.category} />
                  </span>
                  <span>
                    <strong>{library.display_name}</strong>
                    <small>真实物理资源库</small>
                  </span>
                </WorkspaceLink>
                <span className="category-cell">{categoryLabels[library.category]}</span>
                <span className="library-state">
                  <span>
                    <i className={`availability-dot ${library.availability}`} />
                    {library.availability === "online" ? "可访问" : "暂时离线"}
                  </span>
                  <small>{accessLabel(library.access_level)}</small>
                </span>
                <div className="library-row-actions">
                  {admin && (
                    <>
                      <button className="text-button" onClick={() => editCategory(library)}>
                        修改分类
                      </button>
                      <WorkspaceLink route={{ page: "tasks", libraryId: library.library_id }} navigate={navigate}>
                        扫描任务
                      </WorkspaceLink>
                    </>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </>
  );
}
