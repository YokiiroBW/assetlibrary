import { useRef } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { Entry, Library, SearchHitReason } from "./types";

export interface EntryRow {
  entry: Entry;
  library: Library;
  hitReason: SearchHitReason | null;
}

interface VirtualEntryListProps {
  rows: EntryRow[];
  selectedId: string | null;
  onSelect: (row: EntryRow) => void;
  onOpenDirectory: (entry: Entry) => void;
}

export function VirtualEntryList({ rows, selectedId, onSelect, onOpenDirectory }: VirtualEntryListProps) {
  const scrollElement = useRef<HTMLDivElement>(null);
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollElement.current,
    estimateSize: () => 58,
    overscan: 8,
  });

  return (
    <div className="entry-scroll" ref={scrollElement} role="list" aria-label="资产列表">
      <div className="entry-virtual-space" style={{ height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((virtualRow) => {
          const row = rows[virtualRow.index];
          if (row === undefined) return null;
          const isDirectory = row.entry.kind === "directory";
          return (
            <div
              className="entry-positioner"
              data-entry-row
              key={row.entry.entry_id}
              role="listitem"
              style={{ transform: `translateY(${virtualRow.start}px)` }}
            >
              <div className={`entry-row ${selectedId === row.entry.entry_id ? "is-selected" : ""}`}>
                <button
                  className="entry-select"
                  type="button"
                  onClick={() => onSelect(row)}
                  onDoubleClick={() => isDirectory && onOpenDirectory(row.entry)}
                >
                  <span className={`entry-kind ${isDirectory ? "is-folder" : ""}`} aria-hidden="true">
                    {isDirectory ? "▰" : "▱"}
                  </span>
                  <span className="entry-primary">
                    <strong>{row.entry.name}</strong>
                    <small>
                      {row.hitReason === null
                        ? row.entry.relative_path
                        : `${row.library.display_name} · ${row.hitReason === "name" ? "名称命中" : "路径命中"}`}
                    </small>
                  </span>
                </button>
                {isDirectory ? (
                  <button
                    className="open-directory"
                    type="button"
                    onClick={() => onOpenDirectory(row.entry)}
                    aria-label={`打开目录 ${row.entry.name}`}
                  >
                    打开
                  </button>
                ) : (
                  <span className="entry-meta">{formatBytes(row.entry.content_length)}</span>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function formatBytes(value: string | null): string {
  if (value === null) return "—";
  const bytes = Number(value);
  if (!Number.isFinite(bytes)) return value;
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
