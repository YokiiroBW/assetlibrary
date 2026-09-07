import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { AssetLinkClient } from "./assetLinkClient";
import { failure, isAbort, isAccessFailure } from "./hooks/queryState";
import { categoryLabels, isLibraryCategory, libraryCategories } from "./libraryMetadata";
import type { LibraryCategory, RegisterLibraryRequest, StorageSource } from "./types";

export function RegisterLibraryForm({
  client,
  onClose,
  onRegistered,
  onAccessLost,
  initialCategory = "general",
}: {
  client: AssetLinkClient;
  onClose: () => void;
  onRegistered: (libraryId: string) => void;
  onAccessLost: () => void;
  initialCategory?: LibraryCategory;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const nameInput = useRef<HTMLInputElement>(null);
  const active = useRef<AbortController | null>(null);
  const attempt = useRef<{ body: string; key: string } | null>(null);
  const errorElement = useRef<HTMLParagraphElement>(null);
  const [sources, setSources] = useState<StorageSource[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [selectedSourceKey, setSelectedSourceKey] = useState("");
  const [advanced, setAdvanced] = useState(false);
  const selectedSource = sources.find((source) => source.source_key === selectedSourceKey) ?? sources[0];
  const manualPath = advanced || selectedSource?.default_root_path == null;

  useEffect(() => {
    const element = dialog.current;
    const previousFocus = document.activeElement;
    element?.showModal();
    nameInput.current?.focus();
    return () => {
      active.current?.abort();
      element?.close();
      if (previousFocus instanceof HTMLElement && previousFocus.isConnected) previousFocus.focus();
    };
  }, []);
  useEffect(() => {
    if (message !== null) errorElement.current?.focus();
  }, [message]);
  useEffect(() => {
    const controller = new AbortController();
    active.current = controller;
    setLoading(true);
    setMessage(null);
    void client
      .listStorageSources(controller.signal)
      .then((available) => {
        setSources(available);
        setSelectedSourceKey((previous) =>
          available.some((source) => source.source_key === previous) ? previous : (available[0]?.source_key ?? ""),
        );
      })
      .catch((error: unknown) => {
        if (isAbort(error)) return;
        const details = failure(error);
        if (isAccessFailure(details.statusCode)) onAccessLost();
        else setMessage(details.message);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [client, reloadKey, onAccessLost]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (busy || loading || sources.length === 0) return;
    const form = new FormData(event.currentTarget);
    const category = String(form.get("category") ?? "general");
    if (!isLibraryCategory(category)) return;
    const body: RegisterLibraryRequest = {
      source_key: String(form.get("source_key") ?? ""),
      display_name: String(form.get("display_name") ?? ""),
      root_path: manualPath ? String(form.get("root_path") ?? "") : (selectedSource?.default_root_path ?? ""),
      category,
    };
    const encoded = JSON.stringify(body);
    if (attempt.current?.body !== encoded) attempt.current = { body: encoded, key: crypto.randomUUID() };
    const controller = new AbortController();
    active.current?.abort();
    active.current = controller;
    setBusy(true);
    setMessage(null);
    try {
      const libraryId = await client.registerLibrary(body, attempt.current.key, controller.signal);
      if (!controller.signal.aborted) onRegistered(libraryId);
    } catch (error: unknown) {
      if (isAbort(error)) return;
      const details = failure(error);
      if (isAccessFailure(details.statusCode)) onAccessLost();
      else {
        if (details.statusCode === 400 || details.statusCode === 409) attempt.current = null;
        setMessage(details.message);
      }
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  };

  const keepFocusWithin = (event: KeyboardEvent<HTMLDialogElement>) => {
    if (event.key !== "Tab") return;
    const controls = event.currentTarget.querySelectorAll<HTMLElement>(
      "button:not(:disabled), input:not(:disabled), select:not(:disabled)",
    );
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last?.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first?.focus();
    }
  };

  return (
    <dialog
      className="register-dialog"
      ref={dialog}
      onCancel={onClose}
      onKeyDown={keepFocusWithin}
      aria-labelledby="register-heading"
    >
      <form onSubmit={(event) => void submit(event)}>
        <div className="pane-heading">
          <h2 id="register-heading">添加资源库</h2>
          <button className="secondary" type="button" aria-label="关闭添加资源库" onClick={onClose}>
            关闭
          </button>
        </div>
        <p className="form-hint">选择存储源及资源库分类。添加只登记目录，首次扫描由你单独启动。</p>
        {loading && <p role="status">正在读取存储源…</p>}
        {!loading && sources.length === 0 && <p>暂无可用存储源，请联系服务器管理员完成配置。</p>}
        <label htmlFor="storage-source">存储源</label>
        <select
          id="storage-source"
          name="source_key"
          required
          value={selectedSourceKey}
          onChange={(event) => {
            setSelectedSourceKey(event.target.value);
            setAdvanced(false);
          }}
          disabled={loading || busy || sources.length === 0}
        >
          {sources.map((source) => (
            <option value={source.source_key} key={source.source_key}>
              {source.display_name}
            </option>
          ))}
        </select>
        <label>
          资源库名称
          <input ref={nameInput} name="display_name" required maxLength={200} autoComplete="off" disabled={busy} />
        </label>
        <label>
          资源库分类
          <select aria-label="资源库分类" name="category" defaultValue={initialCategory} disabled={busy}>
            {libraryCategories.map((category) => (
              <option key={category} value={category}>
                {categoryLabels[category]}
              </option>
            ))}
          </select>
        </label>
        {selectedSource?.default_root_path && (
          <div className="source-root">
            <span>使用存储源根目录</span>
            <code>{selectedSource.default_root_path}</code>
            <label className="checkbox-line">
              <input
                type="checkbox"
                checked={advanced}
                disabled={busy}
                onChange={(event) => setAdvanced(event.target.checked)}
              />
              高级：指定该范围内的目录
            </label>
          </div>
        )}
        {manualPath && (
          <>
            <label>
              服务器目录
              <input
                key={selectedSourceKey}
                name="root_path"
                defaultValue={selectedSource?.default_root_path ?? ""}
                required
                maxLength={4096}
                autoComplete="off"
                spellCheck={false}
                disabled={busy}
                aria-describedby="root-description"
              />
            </label>
            <p id="root-description" className="form-hint">
              NAS 容器部署：按部署挂载表填写 <code>/assets/photos</code> 等容器路径或其子目录，不要直接填写 NAS
              宿主目录或浏览器电脑的盘符。Windows 原生部署可填写服务器上的 <code>C:\Assets\Photos</code> 或{" "}
              <code>\\nas\photos</code>，完整路径须在所选存储源的允许范围内。
            </p>
          </>
        )}
        {message !== null && (
          <p className="form-error" role="alert" ref={errorElement} tabIndex={-1}>
            {message}
          </p>
        )}
        {message !== null && sources.length === 0 && (
          <button className="secondary" type="button" onClick={() => setReloadKey((value) => value + 1)}>
            重试读取存储源
          </button>
        )}
        <button className="primary" type="submit" disabled={loading || busy || sources.length === 0}>
          {busy ? "正在添加…" : "添加资源库"}
        </button>
      </form>
    </dialog>
  );
}
