import type { AssetLinkClient } from "../assetLinkClient";
import { decodeExportPlan, decodeJob, decodePage, decodeRecheck } from "./dedupResponses";
import type { DedupExportPlan, DedupFindingKind, DedupJob, DedupLimits, DedupPage, DedupRecheck } from "./dedupTypes";

/**
 * The exact-duplicate workbench conversation, expressed as the workbench's own operations over the one
 * AssetLink adapter this Web application has. Every call names a library rather than a physical path, so
 * the server always reads the root it resolved itself; nothing here holds a credential or a file.
 */
export class DedupClient {
  public constructor(private readonly transport: AssetLinkClient) {}

  /**
   * Starts, or re-attaches to, one analysis. The budget is sent only when the page has one to state: the
   * server clamps whatever arrives to its own ceilings and echoes the accepted values back, so the page
   * never has to guess what is being enforced.
   */
  public start(
    libraryId: string,
    operationKey: string,
    retry: boolean,
    signal: AbortSignal,
    limits?: DedupLimits,
  ): Promise<DedupJob> {
    return this.send(
      "start",
      {
        library_id: libraryId,
        operation_key: operationKey,
        retry,
        ...(limits === undefined
          ? {}
          : {
              maximum_files: limits.maximum_files,
              maximum_bytes: limits.maximum_bytes,
              maximum_file_bytes: limits.maximum_file_bytes,
            }),
      },
      signal,
      decodeJob,
    );
  }

  public status(libraryId: string, taskId: string, signal: AbortSignal): Promise<DedupJob> {
    return this.send("status", { library_id: libraryId, task_id: taskId }, signal, decodeJob);
  }

  public results(
    libraryId: string,
    taskId: string,
    options: { kind?: DedupFindingKind; groupKey?: string; cursor?: string | null; pageSize?: number },
    signal: AbortSignal,
  ): Promise<DedupPage> {
    return this.send(
      "results",
      {
        library_id: libraryId,
        task_id: taskId,
        ...(options.kind === undefined ? {} : { kind: options.kind }),
        ...(options.groupKey === undefined ? {} : { group_key: options.groupKey }),
        ...(options.cursor === undefined || options.cursor === null ? {} : { cursor: options.cursor }),
        ...(options.pageSize === undefined ? {} : { page_size: options.pageSize }),
      },
      signal,
      decodePage,
    );
  }

  public cancel(libraryId: string, operationKey: string, signal: AbortSignal): Promise<DedupJob> {
    return this.send("cancel", { library_id: libraryId, operation_key: operationKey }, signal, decodeJob);
  }

  /**
   * Re-checks one report version. The digest is sent so the server rechecks the plan the page is showing
   * rather than whichever version happens to be newest.
   *
   * A recheck runs as a durable background task, so the first answer is a receipt. When `recheckTaskId` is
   * given the call polls that run instead of filing a second one, which is how the page learns the outcome
   * without holding a request open for the whole comparison.
   */
  public revalidate(
    libraryId: string,
    taskId: string,
    planDigest: string,
    operationKey: string,
    signal: AbortSignal,
    recheckTaskId?: string,
  ): Promise<DedupRecheck> {
    return this.send(
      "revalidate",
      {
        library_id: libraryId,
        task_id: taskId,
        plan_digest: planDigest,
        operation_key: operationKey,
        ...(recheckTaskId === undefined ? {} : { recheck_task_id: recheckTaskId }),
      },
      signal,
      decodeRecheck,
    );
  }

  public exportPlan(
    libraryId: string,
    taskId: string,
    analysisVersion: string,
    planDigest: string,
    signal: AbortSignal,
  ): Promise<DedupExportPlan> {
    return this.send(
      "export",
      {
        library_id: libraryId,
        task_id: taskId,
        analysis_version: analysisVersion,
        plan_digest: planDigest,
      },
      signal,
      decodeExportPlan,
    );
  }

  private async send<T>(
    operation: string,
    body: Record<string, unknown>,
    signal: AbortSignal,
    decode: (value: unknown) => T,
  ): Promise<T> {
    return (await this.transport.dedup(operation, body, signal, decode as (value: unknown) => unknown)) as T;
  }
}
