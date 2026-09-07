import { useCallback, useEffect, useRef, useState } from "react";
import { collectionKey, parseWorkspaceRoute, routeHref, type WorkspaceRoute } from "../workspaceRoutes";

export interface WorkspacePosition { scrollTop: number; focusedId: string | null; loadedCount: number }
const emptyPosition: WorkspacePosition = { scrollTop: 0, focusedId: null, loadedCount: 0 };

export function useWorkspaceNavigation() {
  const positions = useRef(new Map<string, WorkspacePosition>());
  const currentKey = useRef(crypto.randomUUID());
  const [state, setState] = useState(() => ({ route: parseWorkspaceRoute(window.location), position: emptyPosition }));
  useEffect(() => {
    currentKey.current = typeof window.history.state?.assetlibraryKey === "string"
      ? window.history.state.assetlibraryKey : crypto.randomUUID();
    window.history.replaceState({ assetlibraryKey: currentKey.current }, "");
    const pop = () => {
      const key = window.history.state?.assetlibraryKey;
      currentKey.current = typeof key === "string" ? key : crypto.randomUUID();
      setState({ route: parseWorkspaceRoute(window.location), position: positions.current.get(currentKey.current) ?? emptyPosition });
    };
    window.addEventListener("popstate", pop);
    return () => window.removeEventListener("popstate", pop);
  }, []);
  const navigate = useCallback((route: WorkspaceRoute, replace = false) => {
    const href = routeHref(route);
    if (href === window.location.pathname + window.location.search) return;
    const position = collectionKey(parseWorkspaceRoute(window.location)) === collectionKey(route)
      ? positions.current.get(currentKey.current) ?? emptyPosition : emptyPosition;
    if (!replace) currentKey.current = crypto.randomUUID();
    window.history[replace ? "replaceState" : "pushState"]({ assetlibraryKey: currentKey.current }, "", href);
    positions.current.set(currentKey.current, position);
    setState({ route, position });
  }, []);
  const remember = useCallback((position: WorkspacePosition) => {
    positions.current.set(currentKey.current, position);
    if (positions.current.size > 100) positions.current.delete(positions.current.keys().next().value!);
  }, []);
  return { ...state, navigate, remember };
}
