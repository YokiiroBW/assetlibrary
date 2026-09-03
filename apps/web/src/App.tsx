import { useEffect, useMemo, useState } from "react";
import { AssetLinkClient } from "./assetLinkClient";
import { useBrowse } from "./hooks/useBrowse";
import { useLibraries } from "./hooks/useLibraries";
import { normalizeSearch, useSearch } from "./hooks/useSearch";
import type { Entry, Library } from "./types";
import { VirtualEntryList, type EntryRow } from "./VirtualEntryList";

export function App() {
  const client = useMemo(() => new AssetLinkClient(), []);
  const libraries = useLibraries(client);
  const [selectedLibraryId, setSelectedLibraryId] = useState<string | null>(null);
  const [parentPath, setParentPath] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const [selection, setSelection] = useState<EntryRow | null>(null);
  const browse = useBrowse(client, selectedLibraryId, parentPath);
  const search = useSearch(client, searchInput);
  const normalizedSearch = normalizeSearch(searchInput);
  const searching = normalizedSearch.length >= 2;
  const selectedLibrary =
    libraries.state.items.find((library) => library.library_id === selectedLibraryId) ??
    (browse.library?.library_id === selectedLibraryId ? browse.library : undefined);

  useEffect(() => {
    if (selectedLibraryId === null && libraries.state.status === "ready" && libraries.state.items.length > 0) {
      setSelectedLibraryId(libraries.state.items[0]?.library_id ?? null);
    }
  }, [libraries.state.items, libraries.state.status, selectedLibraryId]);

  useEffect(() => setSelection(null), [selectedLibraryId, parentPath, normalizedSearch]);

  const sessionExpired = [libraries.state, browse.state, search.state].some((state) => state.statusCode === 401);
  if (sessionExpired) {
    return (
      <SessionExpired
        onRetry={() => {
          libraries.reload();
          browse.reload();
          search.reload();
        }}
      />
    );
  }

  const rows: EntryRow[] = searching
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
  const activeState = searching ? search.state : browse.state;
  const activeLoadMore = searching ? search.loadMore : browse.loadMore;
  const activeReload = searching ? search.reload : browse.reload;

  const chooseLibrary = (library: Library) => {
    setSelectedLibraryId(library.library_id);
    setParentPath("");
    setSearchInput("");
  };
  const openDirectory = (entry: Entry) => {
    if (entry.kind !== "directory") return;
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
            onChange={(event) => setSearchInput(event.target.value)}
            placeholder="搜索文件名或相对路径"
            maxLength={200}
          />
          {searchInput.length > 0 && (
            <button type="button" onClick={() => setSearchInput("")} aria-label="清除搜索">
              ×
            </button>
          )}
        </label>
        <span className="readonly-badge">只读</span>
      </header>

      <div className="workspace">
        <aside className="library-pane" aria-label="资源库">
          <div className="pane-heading">
            <div>
              <span className="eyebrow">工作区</span>
              <h1>资源库</h1>
            </div>
            <span className="loaded-count">{libraries.state.items.length}</span>
          </div>
          {libraries.state.status === "loading" && <Loading label="正在读取授权资源库" />}
          {libraries.state.status === "error" && (
            <Failure message={libraries.state.message} onRetry={libraries.reload} />
          )}
          {libraries.state.status === "ready" && libraries.state.items.length === 0 && (
            <Empty title="没有可见资源库" detail="当前账号尚未获得任何资源库的读取权限。" />
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
            {!searching && parentPath !== "" && (
              <button className="secondary" type="button" onClick={() => setParentPath(parent(parentPath))}>
                返回上级
              </button>
            )}
          </div>
          {!searching && parentPath !== "" && <p className="path-line">/{parentPath}</p>}
          {searchInput.length > 0 && !searching && <Empty title="再输入一个字符" detail="搜索词至少需要 2 个字符。" />}
          {activeState.status === "loading" && <Loading label={searching ? "正在搜索" : "正在读取目录"} />}
          {activeState.status === "error" && <Failure message={activeState.message} onRetry={activeReload} />}
          {activeState.status === "ready" && rows.length === 0 && (
            <Empty
              title={searching ? "没有匹配结果" : "这个目录是空的"}
              detail={searching ? "可以换一个文件名或路径关键词。" : "这里暂时没有可展示的物理条目。"}
            />
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
          {activeState.next_cursor !== null && (
            <button className="load-more secondary" type="button" onClick={activeLoadMore}>
              {activeState.loadingMore ? "载入中…" : "载入更多"}
            </button>
          )}
        </section>

        <aside className="detail-pane" aria-label="资产详情">
          {selection === null ? (
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
      <p className="safety-note">当前切片不会读取文件内容，也不会执行改名、移动或删除。</p>
    </div>
  );
}

function SessionExpired({ onRetry }: { onRetry: () => void }) {
  return (
    <main className="centered-state">
      <div className="state-symbol">↗</div>
      <span className="eyebrow">需要重新认证</span>
      <h1>登录状态已失效</h1>
      <p>请先通过宿主身份系统重新登录，然后再重试。</p>
      <button type="button" onClick={onRetry}>
        重试连接
      </button>
    </main>
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
