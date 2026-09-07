import { useEffect, useRef, useState } from "react";
import { AssetLinkClient } from "../assetLinkClient";
import type { ScanSummary } from "../types";
import { failure, isAbort, isAccessFailure } from "./queryState";

type ScanAction = { kind: "start" | "cancel"; key: string; previousTaskId: string | null };
interface ScanView {
  status: "idle" | "loading" | "ready" | "error";
  scan: ScanSummary | null;
  message: string | null;
  statusCode: number | null;
  pending: boolean;
}

const initial: ScanView = { status: "idle", scan: null, message: null, statusCode: null, pending: false };

export function useLibraryScan(client: AssetLinkClient, libraryId: string | null) {
  const [state, setState] = useState<ScanView>(initial);
  const [refreshKey, setRefreshKey] = useState(0);
  const query = useRef<AbortController | null>(null);
  const mutation = useRef<AbortController | null>(null);
  const action = useRef<ScanAction | null>(null);
  const currentLibrary = useRef(libraryId);

  useEffect(() => {
    currentLibrary.current = libraryId;
    action.current = null;
    setState(libraryId === null ? initial : { ...initial, status: "loading" });
    return () => mutation.current?.abort();
  }, [client, libraryId]);

  useEffect(() => {
    if (libraryId === null) return;
    let disposed = false;
    let timer: number | undefined;
    let reading = false;
    const refresh = async () => {
      if (disposed || document.hidden || reading) return;
      if (mutation.current !== null) {
        timer = window.setTimeout(() => void refresh(), 2_000);
        return;
      }
      reading = true;
      const controller = new AbortController();
      query.current = controller;
      try {
        const result = await client.getLibraryScan(libraryId, controller.signal);
        if (disposed) return;
        const pending = action.current;
        if (
          pending !== null &&
          result.scan !== null &&
          ((pending.kind === "start" && result.scan.task_id !== pending.previousTaskId) ||
            (pending.kind === "cancel" && (result.scan.cancellation_requested || !isActive(result.scan))))
        ) {
          action.current = null;
        }
        setState({ status: "ready", scan: result.scan, message: null, statusCode: null, pending: false });
        if (isActive(result.scan) || action.current !== null) timer = window.setTimeout(() => void refresh(), 2_000);
      } catch (error: unknown) {
        if (isAbort(error) || disposed) return;
        const details = failure(error);
        setState((previous) => ({
          ...previous,
          ...details,
          status: "error",
          scan: isAccessFailure(details.statusCode) ? null : previous.scan,
        }));
        if (!isAccessFailure(details.statusCode)) timer = window.setTimeout(() => void refresh(), 5_000);
      } finally {
        reading = false;
        if (query.current === controller) query.current = null;
        if (!disposed && !document.hidden && controller.signal.aborted)
          timer = window.setTimeout(() => void refresh(), 0);
      }
    };
    const visibilityChanged = () => {
      window.clearTimeout(timer);
      if (document.hidden) query.current?.abort();
      else void refresh();
    };
    document.addEventListener("visibilitychange", visibilityChanged);
    void refresh();
    return () => {
      disposed = true;
      window.clearTimeout(timer);
      query.current?.abort();
      document.removeEventListener("visibilitychange", visibilityChanged);
    };
  }, [client, libraryId, refreshKey]);

  const execute = async (kind: "start" | "cancel") => {
    if (libraryId === null || mutation.current !== null || (kind === "cancel" && state.scan === null)) return;
    const prior = action.current;
    const attempt =
      prior?.kind === kind ? prior : { kind, key: crypto.randomUUID(), previousTaskId: state.scan?.task_id ?? null };
    action.current = attempt;
    query.current?.abort();
    const controller = new AbortController();
    mutation.current = controller;
    setState((previous) => ({ ...previous, pending: true, message: null, statusCode: null }));
    try {
      const result =
        kind === "start"
          ? await client.startLibraryScan(libraryId, attempt.key, controller.signal)
          : await client.cancelLibraryScan(libraryId, attempt.previousTaskId!, attempt.key, controller.signal);
      if (currentLibrary.current !== libraryId || controller.signal.aborted) return;
      action.current = null;
      setState({ status: "ready", scan: result.scan, message: null, statusCode: null, pending: false });
      setRefreshKey((value) => value + 1);
    } catch (error: unknown) {
      if (isAbort(error) || currentLibrary.current !== libraryId) return;
      const details = failure(error);
      if (isAccessFailure(details.statusCode)) action.current = null;
      if (details.statusCode === 400 || details.statusCode === 409) {
        action.current = null;
        setRefreshKey((value) => value + 1);
      }
      setState((previous) => ({
        ...previous,
        ...details,
        status: "error",
        pending: false,
        scan: isAccessFailure(details.statusCode) ? null : previous.scan,
      }));
    } finally {
      if (mutation.current === controller) mutation.current = null;
    }
  };

  return {
    ...state,
    unconfirmed: action.current?.kind ?? null,
    reload: () => setRefreshKey((value) => value + 1),
    start: () => void execute("start"),
    cancel: () => void execute("cancel"),
  };
}

export function isActive(scan: ScanSummary | null): boolean {
  return scan?.state === "queued" || scan?.state === "leased";
}
