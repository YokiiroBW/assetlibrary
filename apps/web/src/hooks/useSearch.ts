import { useCallback, useEffect, useRef, useState } from "react";
import type { AssetLinkClient } from "../assetLinkClient";
import type { PagedState, SearchHit, SearchOptions } from "../types";
import { failedPage, idlePage, isAbort, loadingPage } from "./queryState";

export function normalizeSearch(value: string): string {
  return value.trim().replace(/\s+/g, " ");
}

export function useSearch(
  client: AssetLinkClient,
  query: string,
  options?: SearchOptions,
): { state: PagedState<SearchHit>; loadMore: () => void; reload: () => void } {
  const normalized = normalizeSearch(query);
  const [state, setState] = useState<PagedState<SearchHit>>(idlePage);
  const optionsKey = JSON.stringify(options ?? {});
  const [scope, setScope] = useState({ client, normalized, optionsKey });
  const [reloadKey, setReloadKey] = useState(0);
  const generation = useRef(0);
  const active = useRef<AbortController | null>(null);

  useEffect(() => {
    setScope({ client, normalized, optionsKey });
    const current = ++generation.current;
    active.current?.abort();
    if (normalized.length < 2) {
      setState(idlePage());
      return;
    }
    const controller = new AbortController();
    active.current = controller;
    setState(loadingPage());
    const timer = window.setTimeout(() => {
      void client
        .searchAssets(normalized, null, controller.signal, options)
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
    }, 250);
    return () => {
      window.clearTimeout(timer);
      active.current?.abort();
    };
  }, [client, normalized, optionsKey, reloadKey]);

  const loadMore = useCallback(() => {
    if (normalized.length < 2 || state.next_cursor === null || state.loadingMore) return;
    const current = generation.current;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setState((previous) => ({ ...previous, loadingMore: true }));
    void client
      .searchAssets(normalized, state.next_cursor, controller.signal, options)
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
  }, [client, normalized, optionsKey, state.loadingMore, state.next_cursor]);

  const sameScope = scope.client === client && scope.normalized === normalized && scope.optionsKey === optionsKey;
  const visibleState = sameScope ? state : normalized.length < 2 ? idlePage<SearchHit>() : loadingPage<SearchHit>();
  return { state: visibleState, loadMore, reload: () => setReloadKey((value) => value + 1) };
}
