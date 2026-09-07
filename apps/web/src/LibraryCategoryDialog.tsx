import { useEffect, useRef, useState } from "react";
import type { AssetLinkClient } from "./assetLinkClient";
import { failure, isAbort, isAccessFailure } from "./hooks/queryState";
import { categoryLabels, isLibraryCategory, libraryCategories } from "./libraryMetadata";
import type { Library, LibraryCategory } from "./types";
import { Modal } from "./WorkspacePrimitives";

export function LibraryCategoryDialog({
  library,
  client,
  close,
  changed,
  accessLost,
}: {
  library: Library;
  client: AssetLinkClient;
  close: () => void;
  changed: () => void;
  accessLost: () => void;
}) {
  const [category, setCategory] = useState<LibraryCategory>(library.category);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const attempt = useRef<{ category: LibraryCategory; key: string } | null>(null);
  const active = useRef<AbortController | null>(null);
  const error = useRef<HTMLParagraphElement>(null);
  useEffect(() => () => active.current?.abort(), []);
  useEffect(() => {
    if (message) error.current?.focus();
  }, [message]);
  const save = async () => {
    if (busy || conflict || category === library.category) return;
    if (attempt.current?.category !== category) attempt.current = { category, key: crypto.randomUUID() };
    const controller = new AbortController();
    active.current = controller;
    setBusy(true);
    setMessage(null);
    try {
      await client.updateCategory(
        library.library_id,
        category,
        library.category,
        attempt.current.key,
        controller.signal,
      );
      if (!controller.signal.aborted) changed();
    } catch (reason: unknown) {
      if (isAbort(reason)) return;
      const details = failure(reason);
      if (isAccessFailure(details.statusCode)) accessLost();
      else if (details.statusCode === 409) {
        setConflict(true);
        setMessage("分类已发生变化，请重新读取后再修改。");
      } else setMessage(details.message);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  };
  return (
    <Modal label="修改资源库分类" className="compact-dialog" close={close}>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void save();
        }}
      >
        <p className="form-hint">{library.display_name} · 分类仅用于组织资源库，不移动物理文件。</p>
        <label>
          资源库分类
          <select
            aria-label="资源库分类"
            value={category}
            disabled={busy || conflict}
            onChange={(event) => {
              if (isLibraryCategory(event.target.value)) setCategory(event.target.value);
            }}
          >
            {libraryCategories.map((value) => (
              <option key={value} value={value}>
                {categoryLabels[value]}
              </option>
            ))}
          </select>
        </label>
        {message && (
          <p ref={error} tabIndex={-1} className="form-error" role="alert">
            {message}
          </p>
        )}
        <div className="dialog-actions">
          <button type="button" className="secondary" onClick={close}>
            取消
          </button>
          {conflict ? (
            <button type="button" className="primary" onClick={changed}>
              重新读取
            </button>
          ) : (
            <button className="primary" disabled={busy || category === library.category}>
              {busy ? "正在保存…" : "保存分类"}
            </button>
          )}
        </div>
      </form>
    </Modal>
  );
}
