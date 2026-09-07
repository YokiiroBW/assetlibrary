import { useCallback, useEffect, useRef, useState } from "react";
import type { AssetLinkClient } from "../assetLinkClient";
import type { Library, PagedState } from "../types";
import { failedPage, isAbort, loadingPage } from "./queryState";

export function useLibraries(client: AssetLinkClient): {
  state: PagedState<Library>;
  loadMore: () => void;
  reload: () => void;
} {
  const [state, setState] = useState<PagedState<Library>>(loadingPage);
  const [reloadKey, setReloadKey] = useState(0);
  const generation = useRef(0);
  const active = useRef<AbortController | null>(null);

  useEffect(() => {
    const current = ++generation.current;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setState(loadingPage());
    void client
      .listLibraries(null, controller.signal)
      .then((page) => {
        if (generation.current === current) {
          setState({
            ...page,
            status: "ready",
            message: null,
            statusCode: null,
            loadingMore: false,
          });
        }
      })
      .catch((error: unknown) => {
        if (!isAbort(error) && generation.current === current) {
          setState(failedPage(error));
        }
      });
    return () => active.current?.abort();
  }, [client, reloadKey]);

  const loadMore = useCallback(() => {
    if (state.next_cursor === null || state.loadingMore) return;
    const current = generation.current;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setState((previous) => ({ ...previous, loadingMore: true }));
    void client
      .listLibraries(state.next_cursor, controller.signal)
      .then((page) => {
        if (generation.current === current) {
          setState((previous) => ({
            ...page,
            items: [...previous.items, ...page.items],
            status: "ready",
            message: null,
            statusCode: null,
            loadingMore: false,
          }));
        }
      })
      .catch((error: unknown) => {
        if (!isAbort(error) && generation.current === current) {
          setState((previous) => failedPage(error, previous));
        }
      });
  }, [client, state.loadingMore, state.next_cursor]);

  return {
    state,
    loadMore,
    reload: () => setReloadKey((value) => value + 1),
  };
}
