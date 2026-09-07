import { useLibraryScan } from "./hooks/useLibraryScan";
import type { Availability, ScanState } from "./types";

export function LibraryScanStatus({
  scan,
  availability,
}: {
  scan: ReturnType<typeof useLibraryScan>;
  availability: Availability | undefined;
}) {
  const offline = availability === "offline";
  const summary = scan.scan;
  const title =
    scan.status === "loading"
      ? "正在读取扫描状态"
      : scan.status === "error"
        ? "扫描状态需要确认"
        : summary === null
          ? "尚未建立首次索引"
          : summary.cancellation_requested && (summary.state === "leased" || summary.state === "queued")
            ? "正在取消扫描"
            : stateLabel(summary.state);
  const showStart =
    scan.status === "ready" &&
    scan.unconfirmed === null &&
    (summary === null || summary.can_retry) &&
    summary?.state !== "succeeded";
  return (
    <section className="scan-status" aria-label="首次扫描">
      <div className="scan-copy" role="status" aria-live="polite">
        <strong>{title}</strong>
        {summary !== null ? (
          <span>
            已发现 {summary.observed_entries.toLocaleString("zh-CN")} 项 · 已入库{" "}
            {summary.committed_entries.toLocaleString("zh-CN")} 项
          </span>
        ) : (
          <span>首次扫描只读取目录信息，不会修改原文件。</span>
        )}
        {summary?.state === "failed" && <span>扫描未完成，尚未生成可用索引。检查存储连接后可重试。</span>}
        {summary?.state === "cancelled" && <span>扫描已停止，尚未生成可用索引。</span>}
      </div>
      {scan.message !== null && (
        <p className="form-error" role="alert">
          {scan.message}
        </p>
      )}
      {scan.unconfirmed !== null && !scan.pending && (
        <p className="form-hint">上次提交结果尚未确认，重试会复用同一次请求。</p>
      )}
      <div className="scan-actions">
        {showStart && (
          <button className="secondary" type="button" disabled={scan.pending} onClick={scan.start}>
            {summary === null ? "开始首次扫描" : "重试扫描"}
          </button>
        )}
        {summary?.can_cancel && scan.unconfirmed === null && (
          <button
            className="secondary"
            type="button"
            disabled={scan.pending || summary.cancellation_requested}
            onClick={scan.cancel}
          >
            取消扫描
          </button>
        )}
        {scan.unconfirmed !== null && (
          <button
            className="secondary"
            type="button"
            disabled={scan.pending}
            onClick={scan.unconfirmed === "start" ? scan.start : scan.cancel}
          >
            重试提交
          </button>
        )}
        {scan.status === "error" && (
          <button className="secondary" type="button" disabled={scan.pending} onClick={scan.reload}>
            刷新扫描状态
          </button>
        )}
        {scan.pending && <span role="status">正在提交…</span>}
      </div>
      {offline && showStart && <p className="form-hint">恢复存储连接后可重试，服务器会重新检查目录。</p>}
    </section>
  );
}

function stateLabel(state: ScanState): string {
  return {
    queued: "等待扫描",
    leased: "正在扫描",
    succeeded: "首次扫描已完成",
    failed: "扫描未完成",
    cancelled: "扫描已取消",
  }[state];
}
