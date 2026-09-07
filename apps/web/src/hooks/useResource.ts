import { useCallback, useEffect, useRef, useState } from "react";
import type { AssetLinkClient } from "../assetLinkClient";
import type { EntryDetail, Library, LoadStatus } from "../types";
import { failure, isAbort } from "./queryState";

interface ResourceState<T> { status: LoadStatus; value: T | null; message: string | null; statusCode: number | null }
const empty = { status: "idle", value: null, message: null, statusCode: null } as const;

function useResource<T>(key: string | null, load: (signal: AbortSignal) => Promise<T>) {
  const [state, setState] = useState<ResourceState<T>>(empty);
  const [scope, setScope] = useState({ key, load });
  const [revision, setRevision] = useState(0);
  const generation = useRef(0);
  useEffect(() => {
    const current = ++generation.current;
    const controller = new AbortController();
    setScope({ key, load });
    setState(key === null ? empty : { ...empty, status: "loading" });
    if (key !== null) {
      void load(controller.signal).then((value) => {
        if (current === generation.current && !controller.signal.aborted)
          setState({ status: "ready", value, message: null, statusCode: null });
      }).catch((error: unknown) => {
        if (current === generation.current && !isAbort(error)) setState({ ...empty, status: "error", ...failure(error) });
      });
    }
    return () => controller.abort();
  }, [key, load, revision]);
  return {
    ...(scope.key === key && scope.load === load ? state : { ...empty, status: key === null ? "idle" : "loading" } as ResourceState<T>),
    reload: () => setRevision((value) => value + 1),
  };
}

export function useLibrary(client: AssetLinkClient, id: string | null) {
  const load = useCallback((signal: AbortSignal) => client.getLibrary(id!, signal), [client, id]);
  return useResource<Library>(id, load);
}

export function useEntry(client: AssetLinkClient, libraryId: string | null, entryId: string | null) {
  const load = useCallback((signal: AbortSignal) => client.getEntry(libraryId!, entryId!, signal), [client, libraryId, entryId]);
  return useResource<EntryDetail>(libraryId !== null && entryId !== null ? `${libraryId}:${entryId}` : null, load);
}
