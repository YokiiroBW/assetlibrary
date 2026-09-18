import type { DedupClient } from "./dedupClient";
import type { DedupExportReceipt, DedupJob, DedupPage, DedupRecheck } from "./dedupTypes";

/**
 * The one operation the reader can ask for. It is its own name here because the page and the hook that
 * runs it have to agree on the set: a name the hook does not know is a name it cannot carry out.
 */
export type DedupAction = "start" | "cancel" | "recheck" | "export";

/** What one operation is performed against: the client, the open library, and what is on screen now. */
export interface ActionContext {
  client: DedupClient;
  libraryId: string;
  state: { job: DedupJob | null; page: DedupPage | null };
  operationKey: string;
  signal: AbortSignal;
  settle: (changes: {
    status?: "ready";
    job?: DedupJob;
    page?: null;
    group?: null;
    recheck?: DedupRecheck | null;
    message?: null;
    statusCode?: null;
  }) => void;
  /** Records the operation key the task now on screen belongs to, so later operations name it. */
  adopt: (operationKey: string) => void;
  exported: (receipt: DedupExportReceipt) => void;
}

/**
 * Performs one operation and records only what the server answered. A successful start or cancel never
 * fabricates a result set: the page shows the job's own state and waits for a report. An analysis only
 * ever begins because the reader asked for one; opening the page never starts a run.
 */
export async function performAction(action: DedupAction, context: ActionContext) {
  if (action === "start") {
    // The page restates the budget the server accepted for the version on screen, so a new analysis of
    // the same library runs under the ceiling the reader has already been shown. Before any version
    // exists the page states no budget at all and the server applies its own.
    const limits = context.state.job?.limits;
    // The first request of this mount names the operation by the library, so pressing the button twice
    // without a reload resolves to the same durable task instead of reading the same directories twice.
    const known = await context.client.start(context.libraryId, context.libraryId, false, context.signal, limits);
    // A version that is still running, or one that finished and whose report the server can still serve,
    // is the version this library has. Only a run that already ended without a readable report leaves the
    // reader's request to be carried out as a new analysis under a key of its own.
    const job = retained(known)
      ? known
      : await context.client.start(context.libraryId, context.operationKey, false, context.signal, limits);
    context.adopt(job.task_id === known.task_id ? context.libraryId : context.operationKey);
    context.settle({
      status: "ready",
      job,
      page: null,
      group: null,
      recheck: null,
      message: null,
      statusCode: null,
    });
    return;
  }

  if (action === "cancel") {
    const job = await context.client.cancel(context.libraryId, context.operationKey, context.signal);
    context.settle({ status: "ready", job, message: null, statusCode: null });
    return;
  }
  const job = context.state.job;
  if (job === null) return;
  // Both closing operations are bound to the report on screen: its version and its plan digest. They are
  // read from the page rather than from the recheck's own answer, because a completed recheck files a new
  // version whose digest is the one the page re-read afterwards — the recheck's answer names the version
  // it produced, not the one the reader is now looking at.
  const version = context.state.page?.summary.analysis_version ?? job.analysis_version;
  const digest = context.state.page?.summary.plan_digest ?? "";
  if (action === "recheck") {
    // Filing a recheck answers with a receipt; the page's own effect follows that task to its outcome.
    // The receipt replaces any earlier recheck, so a new request never displays the previous answer.
    const recheck = await context.client.revalidate(
      context.libraryId,
      job.task_id,
      digest,
      context.operationKey,
      context.signal,
    );
    context.settle({ status: "ready", recheck, message: null, statusCode: null });
    return;
  }

  const document = await context.client.exportPlan(context.libraryId, job.task_id, version, digest, context.signal);
  const fileName = `dedup-plan-${job.task_id}.json`;
  save(document, fileName);
  context.exported({
    analysis_version: document.analysis_version,
    file_name: fileName,
    plan_digest: document.plan_digest,
  });
  context.settle({ status: "ready", message: null, statusCode: null });
}

/**
 * Whether the library's own version is worth showing as it stands. A queued or leased analysis is still
 * running, a finished one with a retained report can be read, and a finished one whose report was
 * discarded is still the version the library has: it is reported as unreadable rather than started over.
 */
function retained(job: DedupJob): boolean {
  return job.state === "queued" || job.state === "leased" || job.state === "succeeded";
}

/**
 * Saves the exported plan under a name that names the job it came from. It writes only the plan the
 * server produced: nothing here can move, copy or delete a source file.
 */
function save(document: unknown, fileName: string) {
  const blob = new Blob([JSON.stringify(document, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = window.document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  anchor.rel = "noopener";
  anchor.click();
  URL.revokeObjectURL(url);
}
