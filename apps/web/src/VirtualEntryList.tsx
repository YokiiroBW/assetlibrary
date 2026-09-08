import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { Entry, EntryView, Library, SearchHitReason } from "./types";
import type { useEntrySelection } from "./hooks/useEntrySelection";
import type { WorkspacePosition } from "./hooks/useWorkspaceNavigation";
import { entryType, formatBytes, formatDate } from "./libraryMetadata";
import { WorkspaceIcon } from "./WorkspaceIcon";
import { DerivedImage } from "./DerivedImage";
import type { ImageRequests } from "./imageRequests";

export interface EntryRow {
  entry: Entry;
  library: Library;
  hitReason: SearchHitReason | null;
}

export function VirtualEntryList({
  rows,
  view,
  selection,
  position,
  restoring,
  remember,
  onOpen,
  onQuickLook,
  images,
  imagesAllowed,
}: {
  rows: EntryRow[];
  view: EntryView;
  selection: ReturnType<typeof useEntrySelection>;
  position: WorkspacePosition;
  remember: (position: WorkspacePosition) => void;
  onOpen: (row: EntryRow) => void;
  onQuickLook: (row: EntryRow) => void;
  images: ImageRequests;
  imagesAllowed: boolean;
  restoring: boolean;
}) {
  const scroll = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(640);
  const columns = view === "list" ? 1 : Math.max(2, Math.floor(width / 176));
  const pendingFocus = useRef<string | null>(null);
  const restored = useRef<WorkspacePosition | null>(null);
  const virtualizer = useVirtualizer({
    count: Math.ceil(rows.length / columns),
    getScrollElement: () => scroll.current,
    estimateSize: () => (view === "grid" ? 172 : 56),
    overscan: view === "grid" ? 3 : 8,
  });
  const virtualItems = virtualizer.getVirtualItems();
  useEffect(() => {
    const element = scroll.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => {
      if (entry) setWidth(entry.contentRect.width);
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  useEffect(() => {
    virtualizer.measure();
  }, [view, columns]);
  useLayoutEffect(() => {
    if (restored.current === position || rows.length === 0 || restoring) return;
    restored.current = position;
    virtualizer.scrollToOffset(position.scrollTop);
    const index = rows.findIndex((row) => row.entry.entry_id === position.focusedId);
    if (index >= 0 && (document.activeElement === document.body || scroll.current?.contains(document.activeElement))) {
      pendingFocus.current = position.focusedId;
      virtualizer.scrollToIndex(Math.floor(index / columns), { align: "auto" });
    }
  }, [position, rows.length, columns, restoring]);
  useLayoutEffect(() => {
    if (!pendingFocus.current) return;
    const element = scroll.current?.querySelector<HTMLElement>(`[data-entry-id="${pendingFocus.current}"]`);
    if (element) {
      element.focus({ preventScroll: true });
      pendingFocus.current = null;
    }
  }, [virtualItems]);

  const focusIndex = (index: number, event: KeyboardEvent) => {
    const row = rows[Math.max(0, Math.min(rows.length - 1, index))];
    if (!row) return;
    if (event.ctrlKey || event.metaKey) selection.focus(row.entry.entry_id);
    else selection.select(row.entry.entry_id, event);
    pendingFocus.current = row.entry.entry_id;
    virtualizer.scrollToIndex(Math.floor(Math.max(0, Math.min(rows.length - 1, index)) / columns), { align: "auto" });
  };
  const keyDown = (event: KeyboardEvent<HTMLDivElement>, row: EntryRow, index: number) => {
    const offsets: Record<string, number> = { ArrowDown: columns, ArrowUp: -columns, ArrowRight: 1, ArrowLeft: -1 };
    const offset = offsets[event.key];
    if (offset !== undefined) {
      event.preventDefault();
      focusIndex(index + offset, event);
    } else if (event.key === "Home" || event.key === "End") {
      event.preventDefault();
      focusIndex(event.key === "Home" ? 0 : rows.length - 1, event);
    } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "a") {
      event.preventDefault();
      selection.selectAllLoaded();
    } else if (event.key === " ") {
      event.preventDefault();
      if (event.ctrlKey || event.metaKey || event.shiftKey) selection.select(row.entry.entry_id, event, true);
      else if (!event.repeat) onQuickLook(row);
    } else if (event.key === "Enter") {
      event.preventDefault();
      onOpen(row);
    } else if (event.key === "Escape") {
      event.preventDefault();
      selection.clear();
    }
  };
  return (
    <div
      className={`entry-scroll ${view === "grid" ? "grid-view" : "list-view"}`}
      ref={scroll}
      role="listbox"
      tabIndex={-1}
      aria-label="资产列表"
      aria-multiselectable="true"
      aria-describedby="entry-keyboard-help"
      onScroll={() => {
        if (!restoring)
          remember({
            scrollTop: scroll.current?.scrollTop ?? 0,
            focusedId: selection.focusedId,
            loadedCount: rows.length,
          });
      }}
    >
      <div className="entry-virtual-space" style={{ height: virtualizer.getTotalSize() }}>
        {virtualItems.map((virtualRow) => (
          <div
            className="entry-positioner"
            key={virtualRow.index}
            style={{
              transform: `translateY(${virtualRow.start}px)`,
              height: virtualRow.size,
              gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
            }}
          >
            {rows.slice(virtualRow.index * columns, (virtualRow.index + 1) * columns).map((row, column) => {
              const index = virtualRow.index * columns + column;
              const selected = selection.ids.has(row.entry.entry_id);
              const directory = row.entry.kind === "directory";
              return (
                <div
                  className={`entry-row ${selected ? "is-selected" : ""}`}
                  key={row.entry.entry_id}
                  role="option"
                  aria-selected={selected}
                  aria-label={`${row.entry.name}，${entryType(row.entry)}`}
                  aria-posinset={index + 1}
                  aria-setsize={rows.length}
                  data-entry-row
                  data-entry-id={row.entry.entry_id}
                  tabIndex={
                    selection.focusedId === row.entry.entry_id || (!selection.focusedId && index === 0) ? 0 : -1
                  }
                  onFocus={() => {
                    selection.focus(row.entry.entry_id);
                    remember({
                      scrollTop: scroll.current?.scrollTop ?? 0,
                      focusedId: row.entry.entry_id,
                      loadedCount: rows.length,
                    });
                  }}
                  onClick={(event) => selection.select(row.entry.entry_id, event)}
                  onDoubleClick={() => onOpen(row)}
                  onKeyDown={(event) => keyDown(event, row, index)}
                >
                  <span
                    className={`entry-check ${selected ? "is-checked" : ""}`}
                    aria-hidden="true"
                    onClick={(event) => {
                      event.stopPropagation();
                      selection.select(row.entry.entry_id, event, true);
                    }}
                  >
                    {selected && <WorkspaceIcon name="check" />}
                  </span>
                  <span className={`entry-kind ${directory ? "is-folder" : ""}`} aria-hidden="true">
                    {row.entry.kind === "file" ? (
                      <DerivedImage
                        value={row}
                        requests={images}
                        variant="thumbnail"
                        enabled={imagesAllowed}
                        scrollRoot={scroll}
                      />
                    ) : (
                      <WorkspaceIcon name={directory ? "folder" : "file"} />
                    )}
                    {view === "grid" && row.entry.kind !== "file" && !directory && <span>{entryType(row.entry)}</span>}
                  </span>
                  <span className="entry-primary">
                    <strong title={row.entry.name}>{row.entry.name}</strong>
                    <small>
                      {row.hitReason === null
                        ? directory
                          ? "物理目录"
                          : entryType(row.entry)
                        : `${row.library.display_name} · ${row.hitReason === "name" ? "名称命中" : "路径命中"}`}
                    </small>
                  </span>
                  <span className="entry-type">{entryType(row.entry)}</span>
                  <span className="entry-modified">{formatDate(row.entry.last_write_time_utc)}</span>
                  <span className="entry-size">{formatBytes(row.entry.content_length)}</span>
                </div>
              );
            })}
          </div>
        ))}
      </div>
    </div>
  );
}
