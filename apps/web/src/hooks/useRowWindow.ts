import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";

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

/** The container's own geometry, which is the only thing a fixed-height window has to measure. */
interface RowWindowGeometry {
  offset: number;
  height: number;
}

/**
 * The element the caller's accessor currently names. It is read after every render rather than depended
 * on: a caller writing `getScrollElement: () => node.current` hands over a fresh function on every render,
 * so treating that identity as a change would re-subscribe, re-measure and render again on every pass.
 * Only the element itself is state, and the setter is given the same element back when it has not moved,
 * which is a no-op for React rather than a reason to render.
 */
function useScrollElement(getScrollElement: () => HTMLElement | null): HTMLElement | null {
  const accessor = useRef(getScrollElement);
  accessor.current = getScrollElement;
  const [node, setNode] = useState<HTMLElement | null>(null);
  useLayoutEffect(() => {
    const current = accessor.current();
    setNode((previous) => (previous === current ? previous : current));
  });
  return node;
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
 *
 * Nothing here updates state for its own sake: a scroll, a resize or a re-subscribe only writes the
 * viewport when the measured geometry is actually different, so an idle list renders once and stays put.
 * That matters because the callers pass their scroll accessor inline, and a hook that treated a new
 * function identity as a new subscription would loop: subscribe, measure, write state, render, subscribe.
 */
export function useVirtualizer({
  count,
  getScrollElement,
  estimateSize,
  overscan = defaultOverscan,
}: RowWindowOptions): RowWindow {
  const node = useScrollElement(getScrollElement);
  const [geometry, setGeometry] = useState<RowWindowGeometry>({ offset: 0, height: 0 });
  const measured = useRef(geometry);
  const frame = useRef(0);
  // The accessor is read at call time, never captured, so the callbacks below stay identical across
  // renders even though the caller passes a new function every time.
  const accessor = useRef(getScrollElement);
  accessor.current = getScrollElement;
  const measure = useCallback(() => {
    const element = accessor.current();
    if (!element) return;
    const next = { offset: element.scrollTop, height: element.clientHeight };
    const previous = measured.current;
    // The comparison is what stops the loop: an unchanged geometry is not a state change, so React is
    // not asked to render again.
    if (previous.offset === next.offset && previous.height === next.height) return;
    measured.current = next;
    setGeometry(next);
  }, []);
  useEffect(() => {
    if (!node) return undefined;
    measure();
    // A scroll fires far more often than a frame, so the read is coalesced into one per frame.
    const onScroll = () => {
      if (frame.current !== 0) return;
      frame.current = window.requestAnimationFrame(() => {
        frame.current = 0;
        measure();
      });
    };
    // The observer belongs to this exact element, so a container that is swapped in is observed and the
    // one that was swapped out stops being observed.
    const observer = new ResizeObserver(() => measure());
    observer.observe(node);
    node.addEventListener("scroll", onScroll, { passive: true });
    return () => {
      observer.disconnect();
      node.removeEventListener("scroll", onScroll);
      if (frame.current !== 0) window.cancelAnimationFrame(frame.current);
      frame.current = 0;
    };
  }, [measure, node]);
  const rowHeight = estimateSize();
  const totalSize = count * rowHeight;
  const first = Math.max(0, Math.floor(geometry.offset / rowHeight) - overscan);
  const visible = Math.ceil(geometry.height / rowHeight) + overscan * 2 + 1;
  const rows: RowWindowItem[] = [];
  for (let index = first; index < Math.min(count, first + visible); index++) {
    rows.push({ index, start: index * rowHeight, size: rowHeight });
  }

  const scrollToOffset = useCallback(
    (offset: number) => {
      const element = accessor.current();
      if (!element) return;
      element.scrollTop = Math.max(0, Math.min(offset, Math.max(0, totalSize - element.clientHeight)));
      measure();
    },
    [measure, totalSize],
  );

  const scrollToIndex = useCallback(
    (index: number) => {
      const element = accessor.current();
      if (!element) return;
      const target = Math.max(0, Math.min(index, Math.max(0, count - 1))) * rowHeight;
      const bottom = target + rowHeight;
      // A row already fully on screen is left alone, so a keyboard move one step past the edge scrolls by
      // one row rather than recentring the list under the reader.
      if (target >= element.scrollTop && bottom <= element.scrollTop + element.clientHeight) return;
      scrollToOffset(target < element.scrollTop ? target : bottom - element.clientHeight);
    },
    [count, measure, rowHeight, scrollToOffset],
  );

  return {
    getTotalSize: () => totalSize,
    getVirtualItems: () => rows,
    scrollToOffset,
    scrollToIndex,
  };
}
