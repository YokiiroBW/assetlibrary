import { LibraryScanStatus } from "./LibraryScanStatus";
import { categoryLabels, formatDate } from "./libraryMetadata";
import { browseRoute } from "./workspaceRoutes";
import { EmptyState, ErrorState, LoadingState, WorkspaceLink, type Navigate } from "./WorkspacePrimitives";
import type { Library } from "./types";
import type { useLibraries } from "./hooks/useLibraries";
import type { useLibraryScan } from "./hooks/useLibraryScan";

export function ScanTasksPage({ libraries, library, selectedId, scan, admin, navigate, libraryError, retryLibrary }: {
  libraries: ReturnType<typeof useLibraries>; library: Library | null; selectedId: string | null;
  scan: ReturnType<typeof useLibraryScan>; admin: boolean; navigate: Navigate;
  libraryError: string | null; retryLibrary: () => void;
}) {
  const choices = library && !libraries.state.items.some((item) => item.library_id === library.library_id)
    ? [library, ...libraries.state.items] : libraries.state.items;
  if (!admin) return <section className="catalog-page"><h1>扫描任务</h1><ErrorState message="当前账号没有资源库扫描管理权限。" /></section>;
  return <section className="tasks-page"><div className="page-heading"><div><span className="eyebrow">资源库管理</span><h1>扫描任务</h1>
    <p>选择资源库管理首次扫描。任务状态来自服务器，页面关闭不会取消扫描。</p></div></div>
    <div className="task-scope"><label htmlFor="task-library">资源库</label><select id="task-library" value={selectedId ?? ""}
      onChange={(event) => navigate({ page: "tasks", libraryId: event.target.value || null })}>
      <option value="">选择资源库</option>{choices.map((item) => <option key={item.library_id} value={item.library_id}>{item.display_name}</option>)}
    </select>{libraries.state.next_cursor && <button className="secondary" disabled={libraries.state.loadingMore} onClick={libraries.loadMore}>载入更多资源库</button>}</div>
    {libraries.state.status === "error" && <ErrorState message={libraries.state.message} retry={libraries.reload} />}
    {selectedId === null ? <EmptyState title="选择需要管理的资源库">此页只查询你选择的资源库，不汇总或估算其他任务。</EmptyState>
      : libraryError !== null ? <ErrorState message={libraryError} retry={retryLibrary} />
      : library === null ? <LoadingState label="正在读取资源库" /> : <>
        <div className="task-library-heading"><div><h2>{library.display_name}</h2><p>{categoryLabels[library.category]} · {library.availability === "online" ? "存储可访问" : "存储暂时离线"}</p></div>
          <WorkspaceLink className="secondary" route={browseRoute(library.library_id)} navigate={navigate}>浏览资源库</WorkspaceLink></div>
        <LibraryScanStatus scan={scan} availability={library.availability} />
        {scan.scan && <dl className="task-facts"><div><dt>任务状态</dt><dd>{scan.scan.state === "succeeded" ? "已完成" : scan.scan.state === "failed" ? "失败" : scan.scan.state === "cancelled" ? "已取消" : scan.scan.state === "queued" ? "等待执行" : "执行中"}</dd></div>
          <div><dt>开始时间</dt><dd>{scan.scan.started_at ? formatDate(scan.scan.started_at) : "尚未开始"}</dd></div>
          <div><dt>结束时间</dt><dd>{scan.scan.finished_at ? formatDate(scan.scan.finished_at) : "—"}</dd></div></dl>}
        <div className="task-explanation"><h3>首次扫描会做什么</h3><p>只读发现物理目录并提交索引。失败或取消时，未完成的首次索引不会作为空库发布；可检查存储后重试。</p>
          <p>已完成的首次索引保留快照，本版不提供通用重扫或自动同步后续文件变化。</p></div>
      </>}
  </section>;
}
