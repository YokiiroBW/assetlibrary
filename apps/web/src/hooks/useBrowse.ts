import { useCallback, useEffect, useRef, useState } from "react";
import type { AssetLinkClient } from "../assetLinkClient";
import type { Entry, Library, PagedState } from "../types";
import { failure, idlePage, isAbort, loadingPage } from "./queryState";

export function useBrowse(
  client: AssetLinkClient,
  libraryId: string | null,
  parentPath: string,
): { state: PagedState<Entry>; library: Library | null; loadMore: () => void; reload: () => void } {
  const [state, setState] = useState<PagedState<Entry>>(idlePage);
  const [library, setLibrary] = useState<Library | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const generation = useRef(0);
  const active = useRef<AbortController | null>(null);

  useEffect(() => {
    const current = ++generation.current;
    active.current?.abort();
    if (libraryId === null) {
      setState(idlePage());
      setLibrary(null);
      return;
    }
    const controller = new AbortController();
    active.current = controller;
    setState(loadingPage());
    setLibrary(null);
    void client
      .browseEntries(libraryId, parentPath, null, controller.signal)
      .then((page) => {
        if (generation.current === current) {
          setLibrary(page.library);
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
          setState({ ...loadingPage(), status: "error", ...failure(error) });
        }
      });
    return () => controller.abort();
  }, [client, libraryId, parentPath, reloadKey]);

  const loadMore = useCallback(() => {
    if (libraryId === null || state.next_cursor === null || state.loadingMore) return;
    const current = generation.current;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setState((previous) => ({ ...previous, loadingMore: true }));
    void client
      .browseEntries(libraryId, parentPath, state.next_cursor, controller.signal)
      .then((page) => {
        if (generation.current === current) {
          setLibrary(page.library);
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
          setState((previous) => ({
            ...previous,
            status: "error",
            loadingMore: false,
            ...failure(error),
          }));
        }
      });
  }, [client, libraryId, parentPath, state.loadingMore, state.next_cursor]);

  return { state, library, loadMore, reload: () => setReloadKey((value) => value + 1) };
}
