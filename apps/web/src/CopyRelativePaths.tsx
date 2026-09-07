import { useEffect, useRef, useState } from "react";
import type { EntryDetail } from "./types";
import { Modal } from "./WorkspacePrimitives";

export function CopyRelativePaths({ rows }: { rows: readonly EntryDetail[] }) {
  const [message, setMessage] = useState("");
  const [manual, setManual] = useState<string | null>(null);
  const alive = useRef(true);
  const rowsKey = JSON.stringify(
    rows.map((row) => [row.library.library_id, row.library.display_name, row.entry.relative_path]),
  );
  useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);
  useEffect(() => {
    setMessage("");
    setManual(null);
  }, [rowsKey]);
  const copy = async () => {
    const crossLibrary = new Set(rows.map((row) => row.library.library_id)).size > 1;
    const value = rows
      .map((row) =>
        crossLibrary ? `${row.library.display_name}\t${row.entry.relative_path}` : row.entry.relative_path,
      )
      .join("\n");
    try {
      await navigator.clipboard.writeText(value);
      if (alive.current) setMessage(`已复制 ${rows.length} 项相对路径${crossLibrary ? "（含资源库名）" : ""}`);
    } catch {
      if (alive.current) setManual(value);
    }
  };
  return (
    <span className="copy-path-action">
      <button className="secondary" disabled={rows.length === 0} onClick={() => void copy()}>
        复制相对路径
      </button>
      <span className="copy-status" role="status">
        {message}
      </span>
      {manual !== null && (
        <Modal label="复制相对路径" className="compact-dialog" close={() => setManual(null)}>
          <p className="form-hint">浏览器未允许访问剪贴板，可以选中下方内容手动复制。跨资源库时每行先显示库名。</p>
          <textarea
            className="manual-path-copy"
            aria-label="相对路径"
            readOnly
            value={manual}
            onFocus={(event) => event.target.select()}
            autoFocus
          />
        </Modal>
      )}
    </span>
  );
}
