import { useCallback, useEffect, useRef, useState, type RefObject } from "react";

/** One rendered row, with where it sits in the scroll container. */
export interface RowWindowItem {
  index: number;
  start: number;
  size: number;
}

/** What a caller gets back: the list's own height, the rows to render, and how to move the viewport. */
export interface RowWindow {
  /** The height the scroll container's inner spacer must have for the scrollbar to be honest. */
  getTotalSize: () => number;
  /** The rows to render, in order, with where each one sits. */
  getVirtualItems: () => RowWindowItem[];
  /** Moves the viewport to an absolute offset, clamped to the scrollable range. */
  scrollToOffset: (offset: number) => void;
  /** Brings one row into view, moving no further than it has to. */
  scrollToIndex: (index: number) => void;
}

/** What the two long lists configure, which is all a fixed-height window needs to know. */
export interface RowWindowOptions {
  count: number;
  getScrollElement: () => HTMLElement | null;
  estimateSize: () => number;
  overscan?: number;
}

/** How many rows outside the viewport are rendered, so a fast scroll does not show an empty band. */
const defaultOverscan = 4;

/** Re-reads the container's own geometry, which a resize or a column change invalidates. */
function useViewport(element: RefObject<HTMLElement | null>, recompute: () => void): void {
  useEffect(() => {
    const node = element.current;
    if (!node) return;
    const observer = new ResizeObserver(() => recompute());
    observer.observe(node);
    return () => observer.disconnect();
  }, [element, recompute]);
}

/**
 * The rows of a long list that are actually on screen. It exists because the two long lists in this
 * workspace — the asset list and the library table — need the same three things and nothing else: the
 * height of the whole list, the rows inside the viewport plus a margin, and a way to bring one row back
 * into view after a keyboard move or a restored position.
 *
 * Rows have a fixed height per view, so a row's offset is arithmetic rather than measurement. That is
 * what makes this small enough to keep beside its callers instead of carrying a general-purpose
 * virtualizer: the server bounds every page, so there is no infinite scroll to support and no reason to
 * observe per-row geometry.
 *
 * The shape it answers with is the one the callers were already written against, so replacing what
 * computed the window did not change how either list renders or how a keyboard move scrolls.
 */
export function useVirtualizer({
  count,
  getScrollElement,
  estimateSize,
  overscan = defaultOverscan,
}: RowWindowOptions): RowWindow {
  const element = useRef<HTMLElement | null>(null);
  const [viewport, setViewport] = useState({ offset: 0, height: 0 });
  const frame = useRef(0);
  const recompute = useCallback(() => {
    const node = getScrollElement();
    element.current = node;
    if (!node) return;
    setViewport({ offset: node.scrollTop, height: node.clientHeight });
  }, [getScrollElement]);
  useEffect(() => {
    const node = getScrollElement();
    element.current = node;
    if (!node) return;
    // A scroll fires far more often than a frame, so the read is coalesced into one per frame.
    const onScroll = () => {
      if (frame.current !== 0) return;
      frame.current = window.requestAnimationFrame(() => {
        frame.current = 0;
        recompute();
      });
    };
    recompute();
    node.addEventListener("scroll", onScroll, { passive: true });
    return () => {
      node.removeEventListener("scroll", onScroll);
      if (frame.current !== 0) window.cancelAnimationFrame(frame.current);
      frame.current = 0;
    };
  }, [getScrollElement, recompute]);
  useViewport(element, recompute);
  const rowHeight = estimateSize();
  const totalSize = count * rowHeight;
  const first = Math.max(0, Math.floor(viewport.offset / rowHeight) - overscan);
  const visible = Math.ceil(viewport.height / rowHeight) + overscan * 2 + 1;
  const rows: RowWindowItem[] = [];
  for (let index = first; index < Math.min(count, first + visible); index++) {
    rows.push({ index, start: index * rowHeight, size: rowHeight });
  }

  const scrollToOffset = useCallback(
    (offset: number) => {
      const node = getScrollElement();
      if (!node) return;
      node.scrollTop = Math.max(0, Math.min(offset, Math.max(0, totalSize - node.clientHeight)));
      recompute();
    },
    [getScrollElement, recompute, totalSize],
  );

  const scrollToIndex = useCallback(
    (index: number) => {
      const node = getScrollElement();
      if (!node) return;
      const target = Math.max(0, Math.min(index, Math.max(0, count - 1))) * rowHeight;
      const bottom = target + rowHeight;
      // A row already fully on screen is left alone, so a keyboard move one step past the edge scrolls by
      // one row rather than recentring the list under the reader.
      if (target >= node.scrollTop && bottom <= node.scrollTop + node.clientHeight) return;
      scrollToOffset(target < node.scrollTop ? target : bottom - node.clientHeight);
    },
    [count, getScrollElement, rowHeight, scrollToOffset],
  );

  return {
    getTotalSize: () => totalSize,
    getVirtualItems: () => rows,
    scrollToOffset,
    scrollToIndex,
  };
}
