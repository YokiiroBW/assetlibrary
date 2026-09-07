import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AssetLinkApiError, AssetLinkClient } from "../assetLinkClient";
import type { BrowserSession } from "../types";
import { failure, isAbort, isAccessFailure } from "./queryState";

type SessionState =
  | { status: "checking" }
  | { status: "anonymous"; expired: boolean }
  | { status: "unavailable"; message: string }
  | { status: "authenticated"; session: BrowserSession; generation: number; notice: string | null }
  | { status: "signing_out" | "sign_out_failed"; session: BrowserSession; message: string | null };

export function useSession() {
  const client = useMemo(() => new AssetLinkClient(), []);
  const [state, setState] = useState<SessionState>({ status: "checking" });
  const active = useRef<AbortController | null>(null);
  const revision = useRef(0);
  const workspaceGeneration = useRef(0);
  const previouslyAuthenticated = useRef(false);
  const channel = useRef<BroadcastChannel | null>(null);
  const signingOut = useRef(false);
  const signingIn = useRef(false);

  const accept = useCallback((session: BrowserSession) => {
    previouslyAuthenticated.current = true;
    const nextGeneration = ++workspaceGeneration.current;
    setState((previous) => {
      const sameSession =
        previous.status === "authenticated" &&
        previous.session.principal_id === session.principal_id &&
        previous.session.csrf_token === session.csrf_token &&
        previous.session.is_system_administrator === session.is_system_administrator;
      return {
        status: "authenticated",
        session,
        generation: sameSession ? previous.generation : nextGeneration,
        notice: null,
      };
    });
  }, []);

  const check = useCallback(
    (clear = false) => {
      if (signingOut.current || signingIn.current) return;
      const current = ++revision.current;
      active.current?.abort();
      const controller = new AbortController();
      active.current = controller;
      if (clear) setState({ status: "checking" });
      void client
        .getSession(controller.signal)
        .then((session) => {
          if (revision.current === current) accept(session);
        })
        .catch((error: unknown) => {
          if (isAbort(error) || revision.current !== current) return;
          if (error instanceof AssetLinkApiError && error.status === 401) {
            setState({ status: "anonymous", expired: previouslyAuthenticated.current });
          } else if (error instanceof AssetLinkApiError && isAccessFailure(error.status)) {
            setState({ status: "unavailable", message: failure(error).message });
          } else {
            const message = failure(error).message;
            setState((previous) =>
              previous.status === "authenticated"
                ? { ...previous, notice: `暂时无法核验登录状态。${message}` }
                : { status: "unavailable", message },
            );
          }
        });
    },
    [accept, client],
  );

  useEffect(() => {
    const changes = new BroadcastChannel("assetlibrary-session");
    channel.current = changes;
    changes.onmessage = (event: MessageEvent<unknown>) => {
      if (event.data === "session-changed") check(true);
    };
    const onFocus = () => check();
    window.addEventListener("focus", onFocus);
    check();
    return () => {
      ++revision.current;
      active.current?.abort();
      window.removeEventListener("focus", onFocus);
      changes.close();
      channel.current = null;
    };
  }, [check]);

  const signIn = useCallback(
    async (accountName: string, password: string, signal: AbortSignal) => {
      ++revision.current;
      active.current?.abort();
      signingIn.current = true;
      try {
        const session = await client.signIn(accountName, password, signal);
        signal.throwIfAborted();
        accept(session);
        channel.current?.postMessage("session-changed");
      } finally {
        signingIn.current = false;
      }
    },
    [accept, client],
  );

  const expire = useCallback(() => {
    ++revision.current;
    active.current?.abort();
    setState({ status: "anonymous", expired: true });
    channel.current?.postMessage("session-changed");
  }, []);

  const signOut = useCallback(async (session: BrowserSession) => {
    signingOut.current = true;
    const current = ++revision.current;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    // Hide the old workspace immediately, but await the server before confirming logout.
    setState({ status: "signing_out", session, message: null });
    try {
      await new AssetLinkClient(session.csrf_token).signOut(controller.signal);
      if (revision.current !== current) return;
      previouslyAuthenticated.current = false;
      signingOut.current = false;
      setState({ status: "anonymous", expired: false });
      channel.current?.postMessage("session-changed");
    } catch (error: unknown) {
      if (isAbort(error) || revision.current !== current) return;
      if (error instanceof AssetLinkApiError && error.status === 401) {
        signingOut.current = false;
        setState({ status: "anonymous", expired: true });
        channel.current?.postMessage("session-changed");
      } else {
        setState({ status: "sign_out_failed", session, message: failure(error).message });
      }
    }
  }, []);

  useEffect(() => {
    if (state.status !== "authenticated") return;
    const remaining = Date.parse(state.session.absolute_expires_at) - Date.now();
    const timer = window.setTimeout(
      remaining > 2_147_483_647 ? () => check() : expire,
      Math.max(0, Math.min(remaining, 2_147_483_647)),
    );
    return () => window.clearTimeout(timer);
  }, [state, check, expire]);

  const reconnect = useCallback(() => check(true), [check]);
  return { state, signIn, signOut, expire, reconnect };
}
