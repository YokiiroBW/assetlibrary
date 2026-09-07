import { isLibraryCategory } from "./libraryMetadata";
import type { EntryFilterKind, EntrySort, EntryView, LibraryCategory, SearchOptions, SortDirection } from "./types";

export interface BrowseRoute {
  page: "browse";
  libraryId: string;
  path: string;
  view: EntryView;
  sort: EntrySort;
  direction: SortDirection;
  kind: EntryFilterKind;
  name: string;
  entryId: string | null;
  anchorId: string | null;
}
export interface SearchRoute {
  page: "search";
  query: string;
  scope: SearchOptions["scope"];
  libraryId: string | null;
  path: string;
  view: EntryView;
  entryId: string | null;
  entryLibraryId: string | null;
  fromLibraryId: string | null;
  fromPath: string;
}
export type WorkspaceRoute =
  | { page: "home" }
  | { page: "libraries"; category: LibraryCategory | null }
  | { page: "tasks"; libraryId: string | null }
  | BrowseRoute
  | SearchRoute
  | { page: "invalid" };

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function validIdentifier(value: string): boolean {
  return uuid.test(value) && value !== "00000000-0000-0000-0000-000000000000";
}

export function browseRoute(libraryId: string, path = "", changes: Partial<BrowseRoute> = {}): BrowseRoute {
  return {
    page: "browse",
    libraryId,
    path,
    view: "list",
    sort: "name",
    direction: "asc",
    kind: "all",
    name: "",
    entryId: null,
    anchorId: null,
    ...changes,
  };
}

export function parseWorkspaceRoute(location: Pick<Location, "pathname" | "search">): WorkspaceRoute {
  try {
    if (location.search.length > 8192) return { page: "invalid" };
    const query = new URLSearchParams(location.search);
    const one = (name: string) => {
      if (query.getAll(name).length > 1) throw new Error("Ambiguous route");
      return query.get(name);
    };
    const id = (name: string) => {
      const value = one(name);
      if (value !== null && !validIdentifier(value)) throw new Error("Invalid route identity");
      return value?.toLowerCase() ?? null;
    };
    const path = relativePath(one("path") ?? "");
    const view = choice(one("view"), ["list", "grid"] as const, "list");
    const entryId = id("entry");
    const pathname = location.pathname.replace(/\/$/, "") || "/";
    if (pathname === "/") return { page: "home" };
    if (pathname === "/libraries") return { page: "libraries", category: null };
    const category = /^\/categories\/([^/]+)$/.exec(pathname)?.[1];
    if (category !== undefined)
      return isLibraryCategory(category) ? { page: "libraries", category } : { page: "invalid" };
    const libraryId = /^\/libraries\/([^/]+)$/.exec(pathname)?.[1];
    if (libraryId !== undefined) {
      if (!validIdentifier(libraryId)) return { page: "invalid" };
      return browseRoute(libraryId.toLowerCase(), path, {
        view,
        entryId,
        anchorId: id("anchor"),
        sort: choice(one("sort"), ["name", "modified", "size"] as const, "name"),
        direction: choice(one("direction"), ["asc", "desc"] as const, "asc"),
        kind: choice(one("kind"), ["all", "files", "directories"] as const, "all"),
        name: bounded(one("name") ?? "", 200),
      });
    }
    if (pathname === "/tasks") return { page: "tasks", libraryId: id("library") };
    if (pathname === "/search") {
      const scope = choice(one("scope"), ["all", "library", "directory"] as const, "all");
      const selectedLibrary = id("library");
      const entryLibraryId = id("entry_library");
      const fromLibraryId = id("from_library") ?? selectedLibrary;
      const fromPath = relativePath(one("from_path") ?? (scope === "directory" ? path : ""));
      if (
        (scope === "all" && (selectedLibrary !== null || one("path") !== null)) ||
        (scope !== "all" && selectedLibrary === null) ||
        (scope === "library" && one("path") !== null) ||
        (fromLibraryId === null && fromPath !== "") ||
        (entryId !== null && entryLibraryId === null)
      )
        return { page: "invalid" };
      return {
        page: "search",
        query: bounded(one("q") ?? "", 200),
        scope,
        libraryId: selectedLibrary,
        path,
        view,
        entryId,
        entryLibraryId,
        fromLibraryId,
        fromPath,
      };
    }
    return { page: "invalid" };
  } catch {
    return { page: "invalid" };
  }
}

export function routeHref(route: WorkspaceRoute): string {
  const query = new URLSearchParams();
  const set = (key: string, value: string | null | undefined) => {
    if (value) query.set(key, value);
  };
  let path = "/";
  if (route.page === "libraries") path = route.category === null ? "/libraries" : `/categories/${route.category}`;
  if (route.page === "tasks") {
    path = "/tasks";
    set("library", route.libraryId);
  }
  if (route.page === "browse") {
    path = `/libraries/${route.libraryId}`;
    set("path", route.path);
    set("entry", route.entryId);
    set("anchor", route.anchorId);
    if (route.sort !== "name") set("sort", route.sort);
    if (route.direction !== "asc") set("direction", route.direction);
    if (route.kind !== "all") set("kind", route.kind);
    set("name", route.name);
  }
  if (route.page === "search") {
    path = "/search";
    set("q", route.query);
    set("scope", route.scope);
    if (route.scope !== "all") set("library", route.libraryId);
    if (route.scope === "directory") set("path", route.path);
    set("entry", route.entryId);
    set("entry_library", route.entryId ? route.entryLibraryId : null);
    set("from_library", route.fromLibraryId);
    set("from_path", route.fromLibraryId ? route.fromPath : null);
  }
  if ((route.page === "browse" || route.page === "search") && route.view !== "list") set("view", route.view);
  return path + (query.size > 0 ? `?${query}` : "");
}

// Detail and layout changes share the same collection, including its focus and scroll position.
export function collectionKey(route: WorkspaceRoute): string {
  if (route.page === "browse")
    return JSON.stringify([
      route.page,
      route.libraryId,
      route.path,
      route.sort,
      route.direction,
      route.kind,
      route.name,
      route.anchorId,
    ]);
  if (route.page === "search")
    return JSON.stringify([
      route.page,
      route.query.trim().replace(/\s+/g, " "),
      route.scope,
      route.libraryId,
      route.path,
    ]);
  return routeHref(route);
}

function bounded(value: string, maximum: number): string {
  if (value.length > maximum || /[\u0000-\u001f\u007f]/.test(value)) throw new Error("Invalid route text");
  return value;
}
function relativePath(value: string): string {
  bounded(value, 4096);
  if (
    value !== "" &&
    (value.startsWith("/") ||
      value.includes("\\") ||
      /^[A-Za-z]:/.test(value) ||
      value.split("/").some((part) => part === "" || part === "." || part === ".."))
  )
    throw new Error("Invalid relative route path");
  return value;
}
function choice<T extends string>(value: string | null, allowed: readonly T[], fallback: T): T {
  if (value === null) return fallback;
  const selected = allowed.find((item) => item === value);
  if (selected === undefined) throw new Error("Invalid route choice");
  return selected;
}
