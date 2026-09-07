import { useCallback, useEffect, useRef, useState } from "react";
import type { AssetLinkClient } from "../assetLinkClient";
import type { Library, LibraryCategory, PagedState } from "../types";
import { failedPage, isAbort, loadingPage } from "./queryState";

export function useLibraries(
  client: AssetLinkClient,
  category?: LibraryCategory,
): {
  state: PagedState<Library>;
  loadMore: () => void;
  reload: () => void;
} {
  const [state, setState] = useState<PagedState<Library>>(loadingPage);
  const [scope, setScope] = useState({ client, category });
  const [reloadKey, setReloadKey] = useState(0);
  const generation = useRef(0);
  const active = useRef<AbortController | null>(null);

  useEffect(() => {
    setScope({ client, category });
    const current = ++generation.current;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setState(loadingPage());
    void client
      .listLibraries(null, controller.signal, category)
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
  }, [client, category, reloadKey]);

  const loadMore = useCallback(() => {
    if (state.next_cursor === null || state.loadingMore) return;
    const current = generation.current;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setState((previous) => ({ ...previous, loadingMore: true }));
    void client
      .listLibraries(state.next_cursor, controller.signal, category)
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
  }, [client, category, state.loadingMore, state.next_cursor]);

  return {
    state: scope.client === client && scope.category === category ? state : loadingPage<Library>(),
    loadMore,
    reload: () => setReloadKey((value) => value + 1),
  };
}
