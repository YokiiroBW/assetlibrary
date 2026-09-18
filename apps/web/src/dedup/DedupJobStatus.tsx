import { formatBytes, formatDate } from "../libraryMetadata";
import type { DedupJob, DedupPage } from "./dedupTypes";

const stateLabels: Record<string, string> = {
  queued: "等待执行",
  leased: "正在分析",
  succeeded: "分析完成",
  failed: "分析失败",
  cancelled: "已取消",
};

/**
 * State of the running job and what the server actually measured. There is no progress bar and no
 * percentage: the backend reports processed files and bytes, not a total it can stand behind, so the
 * page shows the counts it has and names the current stage.
 */
export function DedupJobStatus({
  job,
  page,
  pending,
  reportVersion,
  onCancel,
}: {
  job: DedupJob;
  page: DedupPage | null;
  pending: boolean;
  /** The version the server currently holds for this task, which is the one the page is bound to. */
  reportVersion: string | null;
  onCancel: () => void;
}) {
  const statistics = page?.summary.statistics ?? null;
  const plan = page?.summary.plan ?? null;
  // A recheck files a new version of the same task, so the job's own field can name a version the server
  // has already replaced. The version the server named last is stated instead, and it is stated the same
  // way here as it is beside the results, so one version is never shown two different ways.
  const version = reportVersion ?? page?.summary.analysis_version ?? job.analysis_version;
  return (
    <section className="dedup-status" aria-labelledby="dedup-status-heading">
      <h2 id="dedup-status-heading" className="dedup-section-heading">
        当前任务
      </h2>
      <dl className="dedup-facts">
        <div>
          <dt>任务状态</dt>
          <dd>{stateLabels[job.state] ?? job.state}</dd>
        </div>
        <div>
          <dt>资源库</dt>
          <dd>{job.library_display_name}</dd>
        </div>
        <div>
          <dt>结果版本</dt>
          <dd>{version}</dd>
        </div>
        <div>
          <dt>更新时间</dt>
          <dd>{formatDate(job.updated_at)}</dd>
        </div>
        {job.failure_code !== null && (
          <div>
            <dt>失败原因</dt>
            <dd>{job.failure_code}</dd>
          </div>
        )}
        {job.cancellation_requested && (
          <div>
            <dt>取消</dt>
            <dd>已请求取消，仅取消分析本身</dd>
          </div>
        )}
      </dl>
      {statistics !== null && (
        <dl className="dedup-facts">
          <div>
            <dt>已处理文件</dt>
            <dd>{statistics.analyzed_files.toLocaleString("zh-CN")}</dd>
          </div>
          <div>
            <dt>已读取字节</dt>
            <dd>{formatBytes(String(statistics.read_bytes))}</dd>
          </div>
          <div>
            <dt>未读取文件</dt>
            <dd>{statistics.not_read_files.toLocaleString("zh-CN")}</dd>
          </div>
          <div>
            <dt>读取失败</dt>
            <dd>{statistics.failed_files.toLocaleString("zh-CN")}</dd>
          </div>
          <div>
            <dt>跳过文件</dt>
            <dd>{statistics.skipped_files.toLocaleString("zh-CN")}</dd>
          </div>
          <div>
            <dt>发现的目录项</dt>
            <dd>{statistics.observed_entries.toLocaleString("zh-CN")}</dd>
          </div>
        </dl>
      )}
      {plan !== null && <DedupPlanNotice plan={plan} />}
      <div className="dedup-actions">
        <button type="button" className="secondary" onClick={onCancel} disabled={!job.can_cancel || pending}>
          取消分析
        </button>
        {!job.can_cancel && job.state !== "succeeded" && (
          <span className="dedup-note">当前状态不可取消；取消只停止分析，不会改动任何文件。</span>
        )}
      </div>
      <p className="dedup-note">{job.retention_notice}</p>
    </section>
  );
}

/**
 * The report's own completeness statement, rendered as its own block. A partial or bounded scan is
 * never folded into the duplicate count, so "0 组" cannot be read as "no duplicates exist".
 */
function DedupPlanNotice({ plan }: { plan: DedupPage["summary"]["plan"] }) {
  const incomplete = plan.scan_bounds_reached || plan.incomplete_reason_count > 0 || plan.source_failures.length > 0;
  return (
    <div className={incomplete ? "dedup-incomplete" : "dedup-complete"} role="status">
      <h3>本次分析范围</h3>
      <p>{plan.status_text}</p>
      {plan.scan_bounds_reached && <p>本次在达到预算上限后停止，未处理的部分没有参与比较。</p>}
      {plan.source_failures.length > 0 && (
        <p>
          有 {plan.source_failures.length} 个来源本次未能读取（
          {plan.source_failures.map((failure) => failure.reason_code).join("、")}
          ），其结果不代表这些来源的实际情况。
        </p>
      )}
      {plan.incomplete_reason_count > 0 && <p>另有 {plan.incomplete_reason_count} 条范围说明，详见导出计划。</p>}
      {plan.unreadable_paths.length > 0 && (
        <details>
          <summary>本次不可读的路径（{plan.unreadable_paths.length}）</summary>
          <ul>
            {plan.unreadable_paths.map((path) => (
              <li key={path}>{path}</li>
            ))}
          </ul>
        </details>
      )}
      {!incomplete && <p>本次分析覆盖了所选资源库中在预算范围内的全部文件。</p>}
    </div>
  );
}
