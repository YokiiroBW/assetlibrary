import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AssetLinkClient } from "./assetLinkClient";
import { isAccessFailure } from "./hooks/queryState";
import { useBrowse } from "./hooks/useBrowse";
import { useLibraries } from "./hooks/useLibraries";
import { normalizeSearch, useSearch } from "./hooks/useSearch";
import { useLibraryScan } from "./hooks/useLibraryScan";
import { LibraryScanStatus } from "./LibraryScanStatus";
import { RegisterLibraryForm } from "./RegisterLibraryForm";
import type { BrowserSession, Entry, Library } from "./types";
import { VirtualEntryList, type EntryRow } from "./VirtualEntryList";

export function ReadOnlyWorkspace({
  session,
  sessionNotice,
  onReconnect,
  onSessionExpired,
  onSignOut,
}: {
  session: BrowserSession;
  sessionNotice: string | null;
  onReconnect: () => void;
  onSessionExpired: () => void;
  onSignOut: () => void;
}) {
  const client = useMemo(() => new AssetLinkClient(session.csrf_token), [session.csrf_token]);
  const libraries = useLibraries(client);
  const [registering, setRegistering] = useState(false);
  const [selectedLibraryId, setSelectedLibraryId] = useState<string | null>(null);
  const [parentPath, setParentPath] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const [selection, setSelection] = useState<EntryRow | null>(null);
  const libraryAccessFailed = isAccessFailure(libraries.state.statusCode);
  const browse = useBrowse(client, libraryAccessFailed ? null : selectedLibraryId, parentPath);
  const search = useSearch(client, libraryAccessFailed ? "" : searchInput);
  const scan = useLibraryScan(
    client,
    session.is_system_administrator && !libraryAccessFailed ? selectedLibraryId : null,
  );
  const completedScan = useRef<string | null>(null);
  const normalizedSearch = normalizeSearch(searchInput);
  const searching = normalizedSearch.length >= 2;
  const activeState = searching ? search.state : browse.state;
  const sessionExpired = [libraries.state, browse.state, search.state, scan].some((state) => state.statusCode === 401);
  const selectedLibrary =
    isAccessFailure(libraries.state.statusCode) || isAccessFailure(browse.state.statusCode)
      ? undefined
      : (libraries.state.items.find((library) => library.library_id === selectedLibraryId) ??
        (browse.library?.library_id === selectedLibraryId ? browse.library : undefined));

  useEffect(() => {
    if (selectedLibraryId === null && libraries.state.status === "ready" && libraries.state.items.length > 0) {
      setSelectedLibraryId(libraries.state.items[0]?.library_id ?? null);
    }
  }, [libraries.state.items, libraries.state.status, selectedLibraryId]);

  useEffect(() => setSelection(null), [selectedLibraryId, parentPath, normalizedSearch]);

  useEffect(() => {
    if (scan.scan?.state === "succeeded" && completedScan.current !== scan.scan.task_id) {
      completedScan.current = scan.scan.task_id;
      browse.reload();
      libraries.reload();
    }
  }, [scan.scan]);

  useEffect(() => {
    if (sessionExpired) onSessionExpired();
  }, [sessionExpired, onSessionExpired]);
  const registrationAccessLost = useCallback(() => {
    setRegistering(false);
    onReconnect();
  }, [onReconnect]);

  const accessFailed =
    sessionExpired ||
    isAccessFailure(libraries.state.statusCode) ||
    isAccessFailure(activeState.statusCode) ||
    isAccessFailure(scan.statusCode);
  useEffect(() => {
    if (accessFailed) setSelection(null);
  }, [accessFailed]);

  if (sessionExpired) {
    return null;
  }

  const rows: EntryRow[] = accessFailed
    ? []
    : searching
      ? search.state.items.map((hit) => ({
          entry: hit.entry,
          library: hit.library,
          hitReason: hit.hit_reason,
        }))
      : selectedLibrary === undefined
        ? []
        : browse.state.items.map((entry) => ({
            entry,
            library: selectedLibrary,
            hitReason: null,
          }));
  const activeLoadMore = searching ? search.loadMore : browse.loadMore;
  const activeReload = searching ? search.reload : browse.reload;

  const chooseLibrary = (library: Library) => {
    setSelection(null);
    setSelectedLibraryId(library.library_id);
    setParentPath("");
    setSearchInput("");
  };
  const openDirectory = (entry: Entry) => {
    if (entry.kind !== "directory") return;
    setSelection(null);
    setSelectedLibraryId(entry.library_id);
    setParentPath(entry.relative_path);
    setSearchInput("");
  };

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="brand-mark" aria-hidden="true">
          AL
        </div>
        <div className="brand-copy">
          <strong>AssetLibrary</strong>
          <span>只读资产浏览</span>
        </div>
        <label className="search-box">
          <span className="visually-hidden">搜索文件名或相对路径</span>
          <span aria-hidden="true">⌕</span>
          <input
            type="search"
            value={searchInput}
            onChange={(event) => {
              setSelection(null);
              setSearchInput(event.target.value);
            }}
            placeholder="搜索文件名或相对路径"
            maxLength={200}
          />
          {searchInput.length > 0 && (
            <button
              type="button"
              onClick={() => {
                setSelection(null);
                setSearchInput("");
              }}
              aria-label="清除搜索"
            >
              ×
            </button>
          )}
        </label>
        <div className="account-actions">
          <span className="readonly-badge">只读</span>
          <span className="account-name">{session.display_name}</span>
          <button type="button" className="sign-out" onClick={onSignOut}>
            退出登录
          </button>
        </div>
      </header>

      {sessionNotice !== null && (
        <div className="connection-notice" role="alert">
          {sessionNotice}
          <button className="secondary" type="button" onClick={onReconnect}>
            重试连接
          </button>
        </div>
      )}

      {registering && (
        <RegisterLibraryForm
          client={client}
          onClose={() => setRegistering(false)}
          onAccessLost={registrationAccessLost}
          onRegistered={(libraryId) => {
            setRegistering(false);
            setSelectedLibraryId(libraryId);
            setParentPath("");
            setSearchInput("");
            setSelection(null);
            libraries.reload();
          }}
        />
      )}

      <div className="workspace">
        <aside className="library-pane" aria-label="资源库">
          <div className="pane-heading">
            <div>
              <span className="eyebrow">工作区</span>
              <h1>资源库</h1>
            </div>
            <span className="loaded-count">{libraries.state.items.length}</span>
          </div>
          {session.is_system_administrator && (
            <button className="secondary add-library" type="button" onClick={() => setRegistering(true)}>
              添加资源库
            </button>
          )}
          {libraries.state.status === "loading" && <Loading label="正在读取授权资源库" />}
          {libraries.state.status === "error" && (
            <Failure message={libraries.state.message} onRetry={libraries.reload} />
          )}
          {libraries.state.status === "ready" && libraries.state.items.length === 0 && (
            <Empty
              title="没有可见资源库"
              detail={
                session.is_system_administrator
                  ? "添加服务器上的真实目录，然后开始首次扫描。"
                  : "当前账号尚未获得任何资源库的读取权限。"
              }
            />
          )}
          <nav className="library-list">
            {libraries.state.items.map((library) => (
              <button
                type="button"
                key={library.library_id}
                data-library-id={library.library_id}
                className={library.library_id === selectedLibraryId ? "is-current" : ""}
                aria-current={library.library_id === selectedLibraryId ? "page" : undefined}
                onClick={() => chooseLibrary(library)}
              >
                <span className="library-glyph" aria-hidden="true">
                  ◫
                </span>
                <span>
                  <strong>{library.display_name}</strong>
                  <small>{accessLabel(library.access_level)}</small>
                </span>
                <i className={library.availability} aria-label={availabilityLabel(library.availability)} />
              </button>
            ))}
          </nav>
          {libraries.state.next_cursor !== null && (
            <button className="load-more secondary" type="button" onClick={libraries.loadMore}>
              {libraries.state.loadingMore ? "载入中…" : "载入更多资源库"}
            </button>
          )}
        </aside>

        <section className="content-pane" aria-label={searching ? "搜索结果" : "目录内容"}>
          <div className="content-heading">
            <div>
              <span className="eyebrow">{searching ? "跨资源库搜索" : selectedLibrary?.display_name}</span>
              <h2>{searching ? `“${normalizedSearch}”` : parentPath === "" ? "根目录" : leaf(parentPath)}</h2>
            </div>
            <div className="content-actions">
              <button
                className="secondary"
                type="button"
                onClick={() => {
                  activeReload();
                  libraries.reload();
                  scan.reload();
                }}
              >
                刷新
              </button>
              {!searching && parentPath !== "" && (
                <button
                  className="secondary"
                  type="button"
                  onClick={() => {
                    setSelection(null);
                    setParentPath(parent(parentPath));
                  }}
                >
                  返回上级
                </button>
              )}
            </div>
          </div>
          {!searching && parentPath !== "" && <p className="path-line">/{parentPath}</p>}
          {!searching && selectedLibrary?.availability === "offline" && (
            <p className="storage-notice" role="status">
              此资源库暂时离线。这里保留上次成功扫描的索引，恢复连接后可刷新。
            </p>
          )}
          {!searching && selectedLibraryId !== null && session.is_system_administrator && (
            <LibraryScanStatus scan={scan} availability={selectedLibrary?.availability} />
          )}
          {searchInput.length > 0 && !searching && <Empty title="再输入一个字符" detail="搜索词至少需要 2 个字符。" />}
          {activeState.status === "loading" && <Loading label={searching ? "正在搜索" : "正在读取目录"} />}
          {activeState.status === "error" && <Failure message={activeState.message} onRetry={activeReload} />}
          {activeState.status === "ready" &&
            rows.length === 0 &&
            (searching || !session.is_system_administrator || scan.scan?.state === "succeeded") &&
            selectedLibrary?.availability !== "offline" &&
            !accessFailed && (
              <Empty
                title={searching ? "没有匹配结果" : "这个目录是空的"}
                detail={searching ? "可以换一个文件名或路径关键词。" : "这里暂时没有可展示的物理条目。"}
              />
            )}
          {accessFailed && (
            <button className="secondary" type="button" onClick={onReconnect}>
              重新连接
            </button>
          )}
          {rows.length > 0 && (
            <>
              <div className="list-columns" aria-hidden="true">
                <span>名称</span>
                <span>大小 / 操作</span>
              </div>
              <VirtualEntryList
                rows={rows}
                selectedId={selection?.entry.entry_id ?? null}
                onSelect={setSelection}
                onOpenDirectory={openDirectory}
              />
            </>
          )}
          {!accessFailed && activeState.next_cursor !== null && (
            <button className="load-more secondary" type="button" onClick={activeLoadMore}>
              {activeState.loadingMore ? "载入中…" : "载入更多"}
            </button>
          )}
        </section>

        <aside className="detail-pane" aria-label="资产详情">
          {selection === null || accessFailed || activeState.status === "loading" ? (
            <Empty title="选择一个条目" detail="只读详情会显示在这里，不会修改原文件。" />
          ) : (
            <EntryDetails row={selection} />
          )}
        </aside>
      </div>
    </main>
  );
}

function EntryDetails({ row }: { row: EntryRow }) {
  return (
    <div className="details">
      <span className="eyebrow">只读详情</span>
      <div className="detail-icon" aria-hidden="true">
        {row.entry.kind === "directory" ? "▰" : "▱"}
      </div>
      <h2>{row.entry.name}</h2>
      <p className="detail-path">{row.entry.relative_path}</p>
      <dl>
        <div>
          <dt>资源库</dt>
          <dd>{row.library.display_name}</dd>
        </div>
        <div>
          <dt>类型</dt>
          <dd>{kindLabel(row.entry.kind)}</dd>
        </div>
        <div>
          <dt>修改时间</dt>
          <dd>{new Date(row.entry.last_write_time_utc).toLocaleString("zh-CN")}</dd>
        </div>
        <div>
          <dt>权限</dt>
          <dd>{accessLabel(row.library.access_level)}</dd>
        </div>
      </dl>
      <p className="safety-note">当前提供目录与文件信息浏览，原文件保持不变。</p>
    </div>
  );
}

function Loading({ label }: { label: string }) {
  return (
    <div className="inline-state" role="status">
      <span className="spinner" aria-hidden="true" />
      <p>{label}…</p>
    </div>
  );
}

function Failure({ message, onRetry }: { message: string | null; onRetry: () => void }) {
  return (
    <div className="inline-state is-error" role="alert">
      <span aria-hidden="true">!</span>
      <h3>读取失败</h3>
      <p>{message ?? "暂时无法读取资产。"}</p>
      <button className="secondary" type="button" onClick={onRetry}>
        重试
      </button>
    </div>
  );
}

function Empty({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="inline-state is-empty">
      <span aria-hidden="true">·</span>
      <h3>{title}</h3>
      <p>{detail}</p>
    </div>
  );
}

function parent(path: string): string {
  const separator = path.lastIndexOf("/");
  return separator < 0 ? "" : path.slice(0, separator);
}

function leaf(path: string): string {
  return path.slice(path.lastIndexOf("/") + 1);
}

function accessLabel(value: Library["access_level"]): string {
  const labels = {
    read_only: "只读",
    read_write: "读写",
    organize: "整理",
    library_administrator: "资源库管理员",
  } as const;
  return labels[value];
}

function availabilityLabel(value: Library["availability"]): string {
  return value === "online" ? "在线" : "离线";
}

function kindLabel(value: Entry["kind"]): string {
  const labels = {
    file: "文件",
    directory: "目录",
    reparse_file: "重解析文件",
    reparse_directory: "重解析目录",
  } as const;
  return labels[value];
}
