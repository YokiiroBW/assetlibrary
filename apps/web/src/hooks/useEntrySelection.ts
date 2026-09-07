import { useEffect, useState } from "react";

interface SelectionState { scope: string; ids: Set<string>; anchorId: string | null; focusedId: string | null }
interface SelectionModifiers { ctrlKey?: boolean; metaKey?: boolean; shiftKey?: boolean }

export function useEntrySelection(scope: string, rows: readonly { entry: { entry_id: string } }[], initialFocus: string | null) {
  const [state, setState] = useState<SelectionState>(() => ({ scope, ids: new Set(), anchorId: null, focusedId: initialFocus }));
  const current = state.scope === scope ? state : { scope, ids: new Set<string>(), anchorId: null, focusedId: initialFocus };
  useEffect(() => {
    setState((previous) => previous.scope === scope ? previous : { scope, ids: new Set(), anchorId: null, focusedId: initialFocus });
  }, [scope, initialFocus]);
  const select = (id: string, modifiers: SelectionModifiers = {}, toggle = false) => {
    const additive = modifiers.ctrlKey || modifiers.metaKey;
    let ids = new Set<string>();
    const anchor = rows.findIndex((row) => row.entry.entry_id === current.anchorId);
    const index = rows.findIndex((row) => row.entry.entry_id === id);
    if (modifiers.shiftKey && anchor >= 0 && index >= 0) {
      ids = additive ? new Set(current.ids) : ids;
      for (const row of rows.slice(Math.min(anchor, index), Math.max(anchor, index) + 1)) ids.add(row.entry.entry_id);
    } else if (additive || toggle) {
      ids = new Set(current.ids);
      if (ids.has(id)) ids.delete(id); else ids.add(id);
    } else ids.add(id);
    setState({ scope, ids, focusedId: id, anchorId: modifiers.shiftKey && current.anchorId ? current.anchorId : id });
  };
  return {
    ids: current.ids, focusedId: current.focusedId,
    select,
    focus: (id: string) => setState((previous) => ({ ...(previous.scope === scope ? previous : current), scope, focusedId: id })),
    clear: () => setState({ ...current, scope, ids: new Set(), anchorId: null }),
    selectAllLoaded: () => setState({ ...current, scope, ids: new Set(rows.map((row) => row.entry.entry_id)) }),
  };
}
