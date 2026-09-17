import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AssetLinkClient } from "./assetLinkClient";
import { isAccessFailure } from "./hooks/queryState";
import { useBrowse } from "./hooks/useBrowse";
import { useLibraries } from "./hooks/useLibraries";
import { useSearch } from "./hooks/useSearch";
import { useLibraryScan } from "./hooks/useLibraryScan";
import { useEntry, useLibrary } from "./hooks/useResource";
import { useEntrySelection } from "./hooks/useEntrySelection";
import { useNarrowWorkspace } from "./hooks/useNarrowWorkspace";
import { useWorkspaceNavigation } from "./hooks/useWorkspaceNavigation";
import { WorkspaceBreadcrumbs, WorkspaceHeader, WorkspaceSidebar } from "./WorkspaceNavigation";
import { EntryCollectionPage } from "./EntryCollectionPage";
import { EntryDetails } from "./EntryDetails";
import { RegisterLibraryForm } from "./RegisterLibraryForm";
import { LibraryCategoryDialog } from "./LibraryCategoryDialog";
import { ImagePreview } from "./ImagePreview";
import { EmptyState, ErrorState, LoadingState, WorkspaceLink } from "./WorkspacePrimitives";
import { browseRoute, collectionKey, dedupRoute } from "./workspaceRoutes";
import type { DedupExportReceipt } from "./dedup/dedupTypes";
import { parentPath } from "./libraryMetadata";
import type { BrowserSession, BrowseOptions, EntryDetail, Library, SearchOptions } from "./types";
import type { EntryRow } from "./VirtualEntryList";
import { ImageRequests } from "./imageRequests";

/**
 * The three overlays the shell can raise — registering a library, editing its category and the quick
 * look image — are loaded when the reader asks for one. They are dialogs over the view that is already
 * on screen, so nothing needs their bytes until one is opened.
 */

/**
 * The resource-library views — the catalog, its forms and the scan task page — are loaded when the
 * reader opens one of their routes rather than with the workspace. They are whole views the reader only
 * reaches from the navigation, so a reader who stays in the entry lists never pays for their bytes. The
 * shell still owns where they navigate and what they are allowed to do; only their rendering is deferred.
 */
const LibraryAdmin = lazy(async () => {
  const module = await import("./LibraryAdmin");
  return { default: module.LibraryAdmin };
});

/**
 * The exact-duplicate workbench is loaded when the reader opens its route rather than with the shell.
 * The page and its own conversation are a separate view of the same session, so the reader who never
 * opens it never pays for its bytes; the shell hands it the same adapter and route state as before.
 */
const DedupWorkbench = lazy(async () => {
  const module = await import("./dedup/DedupWorkbench");
  return { default: module.DedupWorkbench };
});

export function ReadOnlyWorkspace({
  session,
  sessionNotice,
  imagesAllowed,
  onReconnect,
  onSessionExpired,
  onSignOut,
}: {
  session: BrowserSession;
  sessionNotice: string | null;
  imagesAllowed: boolean;
  onReconnect: () => void;
  onSessionExpired: () => void;
  onSignOut: () => void;
}) {
  const client = useMemo(() => new AssetLinkClient(session.csrf_token), [session.csrf_token]);
  const navigation = useWorkspaceNavigation();
  const { route, navigate } = navigation;
  const scope = collectionKey(route);
  const narrow = useNarrowWorkspace();
  const [mobileNavigation, setMobileNavigation] = useState(false);
  const [registering, setRegistering] = useState(false);
  const [editingCategory, setEditingCategory] = useState<Library | null>(null);
  const [quickLook, setQuickLook] = useState<{ row: EntryRow; route: typeof route } | null>(null);
  const quick = quickLook?.route === route ? quickLook.row : null;
  const [imageRevision, setImageRevision] = useState(0);
  const [dedupExport, setDedupExport] = useState<DedupExportReceipt | null>(null);
  const [imageAccessLoss, setImageAccessLoss] = useState<{ libraryId: string } | null>(null);
  const imageAccessLost = useCallback(
    (status: number, libraryId: string) => {
      if (status === 401) onSessionExpired();
      else setImageAccessLoss({ libraryId });
    },
    [onSessionExpired],
  );
  const images = useMemo(
    () => new ImageRequests(client, imageAccessLost),
    [client, imageAccessLost, imageRevision, scope],
  );
  useEffect(() => () => images.dispose(), [images]);
  const libraries = useLibraries(client, route.page === "libraries" ? (route.category ?? undefined) : undefined);
  const catalogDenied = isAccessFailure(libraries.state.statusCode);
  const currentId =
    route.page === "browse" || route.page === "tasks" || route.page === "search" || route.page === "dedup"
      ? route.libraryId
      : null;
  const resource = useLibrary(client, catalogDenied ? null : currentId);
  const libraryDenied = catalogDenied || isAccessFailure(resource.statusCode);
  const options: BrowseOptions | undefined =
    route.page === "browse"
      ? {
          sort_by: route.sort,
          sort_direction: route.direction,
          kind: route.kind,
          name_filter: route.name,
          ...(route.anchorId ? { anchor_entry_id: route.anchorId } : {}),
        }
      : undefined;
  const searchOptions: SearchOptions | undefined =
    route.page === "search"
      ? {
          scope: route.scope,
          ...(route.scope !== "all" ? { library_id: route.libraryId! } : {}),
          ...(route.scope === "directory" ? { parent_relative_path: route.path } : {}),
        }
      : undefined;
  const browse = useBrowse(
    client,
    route.page === "browse" && !libraryDenied ? route.libraryId : null,
    route.page === "browse" ? route.path : "",
    options,
  );
  const search = useSearch(client, route.page === "search" && !libraryDenied ? route.query : "", searchOptions);
  const scan = useLibraryScan(
    client,
    session.is_system_administrator && !libraryDenied && (route.page === "browse" || route.page === "tasks")
      ? route.libraryId
      : null,
  );
  const active = route.page === "search" ? search : browse;
  const accessFailed = libraryDenied || isAccessFailure(active.state.statusCode) || isAccessFailure(scan.statusCode);
  const library =
    libraryDenied || (route.page === "browse" && isAccessFailure(browse.state.statusCode))
      ? null
      : (browse.library ??
        resource.value ??
        libraries.state.items.find((item) => item.library_id === currentId) ??
        null);
  const rows = useMemo<EntryRow[]>(
    () =>
      accessFailed
        ? []
        : route.page === "search"
          ? search.state.items.map((hit) => ({ entry: hit.entry, library: hit.library, hitReason: hit.hit_reason }))
          : route.page === "browse" && library
            ? browse.state.items.map((entry) => ({ entry, library, hitReason: null }))
            : [],
    [accessFailed, route.page, search.state.items, browse.state.items, library],
  );
  const selection = useEntrySelection(scope, rows, navigation.position.focusedId);
  const openedId = route.page === "browse" || route.page === "search" ? route.entryId : null;
  const selectedRow = selection.ids.size === 1 ? rows.find((row) => selection.ids.has(row.entry.entry_id)) : undefined;
  const detailLibraryId = quick
    ? quick.library.library_id
    : openedId
      ? route.page === "browse"
        ? route.libraryId
        : route.page === "search"
          ? route.entryLibraryId
          : null
      : !narrow
        ? (selectedRow?.library.library_id ?? null)
        : null;
  const detailId = quick?.entry.entry_id ?? openedId ?? (!narrow ? (selectedRow?.entry.entry_id ?? null) : null);
  const detail = useEntry(client, accessFailed ? null : detailLibraryId, accessFailed ? null : detailId);
  const sessionExpired = [libraries.state, resource, browse.state, search.state, scan, detail].some(
    (item) => item.statusCode === 401,
  );
  const lastScan = useRef<{ id: string; state: string } | null>(null);
  const adoptedAnchor = useRef<string | null>(null);
  useEffect(() => {
    if (detailLibraryId && (detail.statusCode === 403 || detail.statusCode === 404))
      images.rejectAccess(detail.statusCode, detailLibraryId);
  }, [detail.statusCode, detailLibraryId, images]);
  useEffect(() => {
    if (!imageAccessLoss) return;
    libraries.reload();
    if (imageAccessLoss.libraryId === currentId) resource.reload();
  }, [imageAccessLoss]);
  useEffect(() => {
    if (sessionExpired) onSessionExpired();
  }, [sessionExpired, onSessionExpired]);
  useEffect(() => {
    if (accessFailed || active.state.status === "loading" || isAccessFailure(detail.statusCode)) selection.clear();
  }, [accessFailed, active.state.status, detail.statusCode]);
  useEffect(() => {
    const target =
      route.page === "browse" ? (route.entryId ?? route.anchorId) : route.page === "search" ? route.entryId : null;
    const key = `${scope}:${target}`;
    if (target && adoptedAnchor.current !== key && rows.some((row) => row.entry.entry_id === target)) {
      adoptedAnchor.current = key;
      selection.select(target);
    }
  }, [scope, openedId, rows]);
  useEffect(() => {
    const current = scan.scan;
    if (
      current?.state === "succeeded" &&
      lastScan.current?.id === current.task_id &&
      lastScan.current.state !== "succeeded"
    ) {
      browse.reload();
      libraries.reload();
      resource.reload();
    }
    lastScan.current = current ? { id: current.task_id, state: current.state } : null;
  }, [scan.scan]);
  useEffect(() => {
    const title =
      route.page === "home"
        ? "首页"
        : route.page === "libraries"
          ? "资源库管理"
          : route.page === "tasks"
            ? "扫描任务"
            : route.page === "dedup"
              ? "精确查重"
              : route.page === "search"
                ? "搜索结果"
                : (library?.display_name ?? "资产浏览");
    document.title = `${title} · AssetLibrary`;
    setMobileNavigation(false);
  }, [scope, library?.display_name]);
  const mutationAccessLost = useCallback(() => {
    setRegistering(false);
    setEditingCategory(null);
    onReconnect();
  }, [onReconnect]);
  if (sessionExpired) return null;
  const closeDetails = () => {
    if (quick) {
      setQuickLook(null);
      return;
    }
    if (route.page === "browse" || route.page === "search") navigate({ ...route, entryId: null }, true);
  };
  const selectionControls = {
    ...selection,
    select: (...args: Parameters<typeof selection.select>) => {
      closeDetails();
      selection.select(...args);
    },
    clear: () => {
      closeDetails();
      selection.clear();
    },
    selectAllLoaded: () => {
      closeDetails();
      selection.selectAllLoaded();
    },
  };
  const openEntry = (row: EntryRow) => {
    if (row.entry.kind === "directory")
      navigate(
        browseRoute(row.library.library_id, row.entry.relative_path, {
          view: route.page === "browse" || route.page === "search" ? route.view : "list",
        }),
      );
    else if (route.page === "browse") navigate({ ...route, entryId: row.entry.entry_id });
    else if (route.page === "search")
      navigate({ ...route, entryId: row.entry.entry_id, entryLibraryId: row.library.library_id });
  };
  const locate = ({ library: target, entry }: EntryDetail) =>
    navigate(
      browseRoute(target.library_id, parentPath(entry.relative_path), {
        anchorId: entry.entry_id,
        view: route.page === "browse" || route.page === "search" ? route.view : "list",
      }),
    );
  const reload = () => {
    setImageRevision((value) => value + 1);
    selection.clear();
    closeDetails();
    active.reload();
    libraries.reload();
    resource.reload();
    scan.reload();
  };
  const collection = route.page === "browse" || route.page === "search";
  return (
    <main className="app-shell">
      <a className="skip-link" href="#workspace-content">
        跳到主要内容
      </a>
      <WorkspaceHeader
        route={route}
        library={library}
        session={session}
        navigate={navigate}
        menu={() => setMobileNavigation(true)}
        signOut={onSignOut}
      />
      {sessionNotice && (
        <div className="connection-notice" role="alert">
          {sessionNotice}
          <button className="secondary" onClick={onReconnect}>
            重试连接
          </button>
        </div>
      )}
      <div className="workspace">
        <WorkspaceSidebar
          route={route}
          library={library}
          libraries={libraries.state.items}
          navigate={navigate}
          mobileOpen={mobileNavigation}
          closeMobile={() => setMobileNavigation(false)}
        />
        <div className="workspace-main" id="workspace-content" tabIndex={-1}>
          <WorkspaceBreadcrumbs route={route} library={library} navigate={navigate} />
          <div className={`page-layout ${collection && !narrow ? "with-details" : ""}`}>
            {(route.page === "home" || route.page === "libraries" || route.page === "tasks") && (
              <Suspense fallback={<LoadingState label="正在载入资源库管理" />}>
                <LibraryAdmin
                  route={route.page}
                  category={route.page === "libraries" ? route.category : null}
                  libraryId={route.page === "tasks" ? route.libraryId : null}
                  libraries={libraries}
                  library={library}
                  libraryError={resource.status === "error" ? resource.message : null}
                  retryLibrary={resource.reload}
                  scan={scan}
                  admin={session.is_system_administrator}
                  navigate={navigate}
                  register={() => setRegistering(true)}
                  editCategory={setEditingCategory}
                />
              </Suspense>
            )}
            {route.page === "dedup" && (
              <Suspense fallback={<LoadingState label="正在载入精确查重工作台" />}>
                <DedupWorkbench
                  client={client}
                  libraries={libraries}
                  library={library}
                  libraryError={resource.status === "error" ? resource.message : null}
                  retryLibrary={resource.reload}
                  admin={session.is_system_administrator}
                  section={route.kind}
                  selectedId={route.libraryId}
                  exported={dedupExport}
                  onExported={setDedupExport}
                  onSelectLibrary={(libraryId) => navigate(dedupRoute(libraryId || null))}
                  onSelectSection={(section) => navigate(dedupRoute(route.libraryId, { kind: section }))}
                />
              </Suspense>
            )}
            {collection && (
              <div className="collection-wrapper">
                {libraries.state.status === "error" && (
                  <ErrorState message={libraries.state.message} retry={libraries.reload} />
                )}
                {resource.status === "error" && <ErrorState message={resource.message} retry={resource.reload} />}
                <EntryCollectionPage
                  key={scope}
                  route={route}
                  library={library}
                  state={active.state}
                  rows={rows}
                  selection={selectionControls}
                  scan={scan}
                  admin={session.is_system_administrator}
                  accessFailed={accessFailed}
                  navigate={navigate}
                  loadMore={active.loadMore}
                  reload={reload}
                  reconnect={onReconnect}
                  open={openEntry}
                  quickLook={(row) => setQuickLook({ row, route })}
                  images={images}
                  imagesAllowed={imagesAllowed && !accessFailed && !openedId && !quick}
                  position={navigation.position}
                  remember={navigation.remember}
                />
              </div>
            )}
            {collection && (!narrow || (!openedId && !quick)) && (
              <EntryDetails
                detail={detail}
                selectedRows={rows.filter((row) => selection.ids.has(row.entry.entry_id))}
                narrow={narrow}
                opened={openedId !== null}
                close={closeDetails}
                locate={locate}
                openDirectory={({ library: target, entry }) =>
                  navigate(browseRoute(target.library_id, entry.relative_path, { view: route.view }))
                }
              />
            )}
            {route.page === "invalid" && (
              <section className="invalid-route">
                <EmptyState title="无法打开此地址">页面路径或查询参数无效，请从资源库导航重新进入。</EmptyState>
                <WorkspaceLink className="primary" route={{ page: "home" }} navigate={navigate}>
                  回到首页
                </WorkspaceLink>
              </section>
            )}
          </div>
        </div>
      </div>
      {collection && (openedId !== null || quick !== null) && (
        <ImagePreview
          detail={detail}
          requests={images}
          enabled={imagesAllowed && !accessFailed}
          quick={quick !== null}
          close={closeDetails}
          locate={locate}
          openDirectory={({ library: target, entry }) =>
            navigate(browseRoute(target.library_id, entry.relative_path, { view: route.view }))
          }
        />
      )}
      {registering && (
        <RegisterLibraryForm
          client={client}
          initialCategory={route.page === "libraries" ? (route.category ?? "general") : "general"}
          onClose={() => setRegistering(false)}
          onAccessLost={mutationAccessLost}
          onRegistered={(id) => {
            setRegistering(false);
            libraries.reload();
            navigate(browseRoute(id));
          }}
        />
      )}
      {editingCategory && (
        <LibraryCategoryDialog
          client={client}
          library={editingCategory}
          close={() => setEditingCategory(null)}
          accessLost={mutationAccessLost}
          changed={() => {
            setEditingCategory(null);
            libraries.reload();
            resource.reload();
          }}
        />
      )}
    </main>
  );
}
