import type { useLibraries } from "./hooks/useLibraries";
import type { useLibraryScan } from "./hooks/useLibraryScan";
import { LibraryCatalogPage } from "./LibraryCatalogPage";
import { ScanTasksPage } from "./ScanTasksPage";
import type { Library } from "./types";
import type { WorkspaceRoute } from "./workspaceRoutes";

/**
 * The resource-library views of the workspace: the catalog, the scan task page and the routes that lead
 * between them. They are one unit because the shell only reaches them from the navigation and they share
 * the same catalog state, so deferring them together costs the reader one request when they ask for one
 * and nothing at all when they do not.
 *
 * Nothing here decides what a library is or who may change one: the shell passes its own session state in
 * and receives the reader's intent back as navigation and callbacks.
 */
export function LibraryAdmin({
  route,
  category,
  libraryId,
  libraries,
  library,
  libraryError,
  retryLibrary,
  scan,
  admin,
  navigate,
  register,
  editCategory,
}: {
  route: "home" | "libraries" | "tasks";
  category: Parameters<typeof LibraryCatalogPage>[0]["category"];
  libraryId: string | null;
  libraries: ReturnType<typeof useLibraries>;
  library: Library | null;
  libraryError: string | null;
  retryLibrary: () => void;
  scan: ReturnType<typeof useLibraryScan>;
  admin: boolean;
  navigate: (route: WorkspaceRoute) => void;
  register: () => void;
  editCategory: (library: Library) => void;
}) {
  if (route === "tasks") {
    return (
      <ScanTasksPage
        libraries={libraries}
        library={library}
        selectedId={libraryId}
        libraryError={libraryError}
        retryLibrary={retryLibrary}
        scan={scan}
        admin={admin}
        navigate={navigate}
      />
    );
  }

  return (
    <LibraryCatalogPage
      home={route === "home"}
      category={category}
      libraries={libraries}
      admin={admin}
      navigate={navigate}
      register={register}
      editCategory={editCategory}
    />
  );
}
