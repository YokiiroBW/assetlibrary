import { useEffect, useState } from "react";
import { categoryLabels, libraryCategories } from "./libraryMetadata";
import type { BrowserSession, Library } from "./types";
import { browseRoute, type SearchRoute, type WorkspaceRoute } from "./workspaceRoutes";
import { WorkspaceIcon } from "./WorkspaceIcon";
import { Modal, WorkspaceLink, type Navigate } from "./WorkspacePrimitives";

export function WorkspaceSidebar({
  route,
  library,
  libraries,
  navigate,
  mobileOpen,
  closeMobile,
}: {
  route: WorkspaceRoute;
  library: Library | null;
  libraries: readonly Library[];
  navigate: Navigate;
  mobileOpen: boolean;
  closeMobile: () => void;
}) {
  const shortcuts = libraries.slice(0, 7).map((item) => (item.library_id === library?.library_id ? library : item));
  if (library && !shortcuts.some((item) => item.library_id === library.library_id)) shortcuts.unshift(library);
  const links = (
    <>
      <nav className="primary-nav" aria-label="工作区导航">
        <WorkspaceLink
          route={{ page: "home" }}
          navigate={navigate}
          current={route.page === "home"}
          onFollow={closeMobile}
        >
          <WorkspaceIcon name="home" />
          首页
        </WorkspaceLink>
        <WorkspaceLink
          route={{ page: "libraries", category: null }}
          navigate={navigate}
          current={route.page === "libraries" && route.category === null}
          onFollow={closeMobile}
        >
          <WorkspaceIcon name="folder" />
          资源库管理
        </WorkspaceLink>
        <WorkspaceLink
          route={{ page: "tasks", libraryId: null }}
          navigate={navigate}
          current={route.page === "tasks"}
          onFollow={closeMobile}
        >
          <WorkspaceIcon name="tasks" />
          扫描任务
        </WorkspaceLink>
      </nav>
      <div className="nav-label">
        资源分类 <span>聚合视图</span>
      </div>
      <nav className="category-nav" aria-label="资源分类">
        {libraryCategories.map((category) => (
          <WorkspaceLink
            key={category}
            route={{ page: "libraries", category }}
            navigate={navigate}
            onFollow={closeMobile}
            current={
              (route.page === "libraries" && route.category === category) ||
              (route.page === "browse" && library?.category === category)
            }
          >
            <WorkspaceIcon name={category} className={`category-${category}`} />
            {categoryLabels[category]}
          </WorkspaceLink>
        ))}
      </nav>
      <div className="nav-label">资源库快捷入口</div>
      <nav className="library-list" aria-label="已加载资源库">
        {shortcuts.map((item) => (
          <WorkspaceLink
            key={item.library_id}
            route={browseRoute(item.library_id)}
            navigate={navigate}
            current={route.page === "browse" && route.libraryId === item.library_id}
            onFollow={closeMobile}
          >
            <WorkspaceIcon name="folder" />
            <span className="nav-library-name">{item.display_name}</span>
            <i
              className={`availability-dot ${item.availability}`}
              aria-label={item.availability === "online" ? "在线" : "离线"}
            />
          </WorkspaceLink>
        ))}
      </nav>
      <WorkspaceLink
        className="all-libraries-link"
        route={{ page: "libraries", category: null }}
        navigate={navigate}
        onFollow={closeMobile}
      >
        查看全部资源库
        <WorkspaceIcon name="chevron" />
      </WorkspaceLink>
      <div className="sidebar-note">
        <WorkspaceIcon name="info" />
        <p>
          当前浏览首次扫描索引。
          <br />
          资产原文件保持只读。
        </p>
      </div>
    </>
  );
  return (
    <>
      <aside className="workspace-sidebar" aria-label="资源库导航">
        {links}
      </aside>
      {mobileOpen && (
        <Modal label="工作区导航" className="navigation-drawer" close={closeMobile}>
          {links}
        </Modal>
      )}
    </>
  );
}

export function WorkspaceHeader({
  route,
  library,
  session,
  navigate,
  menu,
  signOut,
}: {
  route: WorkspaceRoute;
  library: Library | null;
  session: BrowserSession;
  navigate: Navigate;
  menu: () => void;
  signOut: () => void;
}) {
  const [query, setQuery] = useState(route.page === "search" ? route.query : "");
  const [scope, setScope] = useState<SearchRoute["scope"]>(
    route.page === "search" ? route.scope : route.page === "browse" ? "library" : "all",
  );
  useEffect(() => {
    setQuery(route.page === "search" ? route.query : "");
    setScope(route.page === "search" ? route.scope : route.page === "browse" ? "library" : "all");
  }, [route]);
  const libraryId = route.page === "browse" ? route.libraryId : route.page === "search" ? route.fromLibraryId : null;
  const directory = route.page === "browse" ? route.path : route.page === "search" ? route.fromPath : "";
  const search = (value: string, selectedScope: SearchRoute["scope"], replace: boolean) => {
    navigate(
      {
        page: "search",
        query: value,
        scope: selectedScope,
        libraryId: selectedScope === "all" ? null : libraryId,
        path: selectedScope === "directory" ? directory : "",
        fromLibraryId: libraryId,
        fromPath: directory,
        view: route.page === "browse" || route.page === "search" ? route.view : "list",
        entryId: null,
        entryLibraryId: null,
      },
      replace,
    );
  };
  return (
    <header className="topbar">
      <button className="icon-button mobile-menu" aria-label="打开工作区导航" onClick={menu}>
        <WorkspaceIcon name="menu" />
      </button>
      <WorkspaceLink className="brand" route={{ page: "home" }} navigate={navigate}>
        <span className="brand-mark">
          <WorkspaceIcon name="general" />
        </span>
        <strong>AssetLibrary</strong>
      </WorkspaceLink>
      <form
        className="global-search"
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          search(query, scope, route.page === "search");
        }}
      >
        <WorkspaceIcon name="search" />
        <input
          type="search"
          aria-label="搜索文件名或相对路径"
          placeholder="搜索文件名或相对路径"
          maxLength={200}
          value={query}
          onChange={(event) => {
            const value = event.target.value;
            setQuery(value);
            search(value, scope, route.page === "search");
          }}
        />
        <select
          aria-label="搜索范围"
          value={scope}
          onChange={(event) => {
            const value = event.target.value as SearchRoute["scope"];
            setScope(value);
            search(query, value, false);
          }}
        >
          <option value="all">全部资源库</option>
          {libraryId && (
            <>
              <option value="library">当前资源库</option>
              <option value="directory">当前目录及子目录</option>
            </>
          )}
        </select>
      </form>
      <div className="account-actions">
        <span className="readonly-badge">只读索引</span>
        <span className="account-avatar" aria-hidden="true">
          {session.display_name.slice(0, 1)}
        </span>
        <span className="account-name">{session.display_name}</span>
        <button className="icon-button sign-out" aria-label="退出登录" title="退出登录" onClick={signOut}>
          <WorkspaceIcon name="logout" />
        </button>
      </div>
      {library && <span className="visually-hidden">当前资源库：{library.display_name}</span>}
    </header>
  );
}

export function WorkspaceBreadcrumbs({
  route,
  library,
  navigate,
}: {
  route: WorkspaceRoute;
  library: Library | null;
  navigate: Navigate;
}) {
  const segments: { label: string; route?: WorkspaceRoute }[] = [{ label: "资产库", route: { page: "home" } }];
  if (route.page === "libraries")
    segments.push({ label: route.category ? categoryLabels[route.category] : "资源库管理" });
  if (route.page === "tasks") segments.push({ label: "扫描任务" });
  if (route.page === "search") segments.push({ label: "搜索结果" });
  if (route.page === "browse") {
    if (library)
      segments.push({
        label: categoryLabels[library.category],
        route: { page: "libraries", category: library.category },
      });
    segments.push({ label: library?.display_name ?? "资源库", route: browseRoute(route.libraryId) });
    let path = "";
    for (const segment of route.path.split("/").filter(Boolean)) {
      path = path ? `${path}/${segment}` : segment;
      segments.push({ label: segment, route: browseRoute(route.libraryId, path, { view: route.view }) });
    }
  }
  return (
    <nav className="breadcrumbs" aria-label="面包屑">
      <ol>
        {segments.map((segment, index) => (
          <li key={index}>
            {index > 0 && <WorkspaceIcon name="chevron" />}
            {segment.route && index < segments.length - 1 ? (
              <WorkspaceLink route={segment.route} navigate={navigate}>
                {segment.label}
              </WorkspaceLink>
            ) : (
              <span aria-current="page">{segment.label}</span>
            )}
          </li>
        ))}
      </ol>
    </nav>
  );
}
