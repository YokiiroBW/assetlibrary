import { useMemo } from "react";
import type { AssetLinkClient } from "../assetLinkClient";
import { useDedupJob } from "../hooks/useDedupJob";
import type { useLibraries } from "../hooks/useLibraries";
import type { DedupSection } from "../workspaceRoutes";
import type { Library } from "../types";
import { DedupClient } from "./dedupClient";
import type { DedupExportReceipt, DedupFindingKind } from "./dedupTypes";
import { DedupPage } from "./DedupPage";

/**
 * The exact-duplicate workbench and the session state it owns: its own adapter, its own job handle and
 * the export receipt. It is one component because that state must live exactly as long as the page does
 * — a recheck outlives the press that filed it and is still running when the reader looks away — and
 * because the workbench is a view the shell only reaches from its own route.
 *
 * It speaks through the shared AssetLink adapter, so the browser keeps exactly one place that reaches
 * the network and one place that attaches the session's CSRF header.
 */
export function DedupWorkbench({
  client,
  libraries,
  library,
  libraryError,
  retryLibrary,
  admin,
  section,
  selectedId,
  exported,
  onExported,
  onSelectLibrary,
  onSelectSection,
}: {
  client: AssetLinkClient;
  libraries: ReturnType<typeof useLibraries>;
  library: Library | null;
  libraryError: string | null;
  retryLibrary: () => void;
  admin: boolean;
  section: DedupSection;
  selectedId: string | null;
  exported: DedupExportReceipt | null;
  onExported: (receipt: DedupExportReceipt) => void;
  onSelectLibrary: (libraryId: string) => void;
  onSelectSection: (section: DedupSection) => void;
}) {
  const dedupClient = useMemo(() => new DedupClient(client), [client]);
  const dedupJob = useDedupJob(dedupClient, selectedId, findingKind(section), onExported);
  return (
    <DedupPage
      libraries={libraries}
      library={library}
      libraryError={libraryError}
      retryLibrary={retryLibrary}
      admin={admin}
      section={section}
      selectedId={selectedId}
      dedup={dedupJob}
      exported={exported}
      onSelectLibrary={onSelectLibrary}
      onSelectSection={onSelectSection}
      onStart={dedupJob.start}
    />
  );
}

/**
 * What the shell hands the workbench. The shell builds this from its own session and route state, so it
 * never names a workbench-internal type to reach the page.
 */
export type DedupWorkbenchProps = Parameters<typeof DedupWorkbench>[0];

/**
 * Maps the route's section name onto the server's finding kind. Navigation only carries the name of the
 * open section; which kinds exist and what they mean is the contract's business.
 */
function findingKind(section: DedupSection): DedupFindingKind {
  return section === "groups" ? "ByteDuplicateGroup" : section === "unverified" ? "Unverified" : "Unreadable";
}
