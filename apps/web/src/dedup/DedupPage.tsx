import { useEffect, useRef } from "react";
import { formatBytes, formatDate } from "../libraryMetadata";
import { isAccessFailure } from "../hooks/queryState";
import type { DedupJobHandle } from "../hooks/useDedupJob";
import type { useLibraries } from "../hooks/useLibraries";
import { dedupKinds } from "./dedupTypes";
import type { DedupExportReceipt, DedupRecheck, DedupSummary } from "./dedupTypes";
import { DedupAnalysisForm } from "./DedupAnalysisForm";
import { DedupGroupDetail } from "./DedupGroupDetail";
import { DedupJobStatus } from "./DedupJobStatus";
import { DedupResultList } from "./DedupResultList";
import { EmptyState, ErrorState, LoadingState } from "../WorkspacePrimitives";
import type { DedupSection } from "../workspaceRoutes";
import type { Library } from "../types";

/**
 * The budget this page states before a run starts. It mirrors the server's accepted ceilings so the
 * reader knows in advance that a bounded scan is a normal outcome, not a failure.
 */
const limits = {
  maximum_files: 200_000,
  maximum_bytes: 2_147_483_648,
  maximum_file_bytes: 268_435_456,
  hash_concurrency: 4,
};

/**
 * The exact-duplicate workbench. Its sections appear in one fixed order — source and budget, start,
 * current job, results, the open group's detail, then recheck and export — so the reader always finds
 * the same thing in the same place, and every section says which report version it describes.
 */
export function DedupPage({
  libraries,
  library,
  libraryError,
  retryLibrary,
  admin,
  section,
  selectedId,
  dedup,
  recheck,
  exported,
  onSelectLibrary,
  onSelectSection,
  onStart,
}: {
  libraries: ReturnType<typeof useLibraries>;
  library: Library | null;
  libraryError: string | null;
  retryLibrary: () => void;
  admin: boolean;
  section: DedupSection;
  selectedId: string | null;
  dedup: DedupJobHandle;
  recheck: DedupRecheck | null;
  exported: DedupExportReceipt | null;
  onSelectLibrary: (libraryId: string) => void;
  onSelectSection: (section: DedupSection) => void;
  onStart: () => void;
}) {
  if (!admin) {
    return (
      <section className="dedup-page">
        <DedupHeading />
        <ErrorState message="当前账号没有精确查重权限。" />
      </section>
    );
  }

  const choices =
    library && !libraries.state.items.some((item) => item.library_id === library.library_id)
      ? [library, ...libraries.state.items]
      : libraries.state.items;
  const scanning = dedup.job !== null && (dedup.job.state === "queued" || dedup.job.state === "leased");

  return (
    <section className="dedup-page">
      <DedupHeading />
      {libraries.state.status === "error" && !isAccessFailure(libraries.state.statusCode) && (
        <ErrorState message={libraries.state.message} retry={libraries.reload} />
      )}
      <DedupAnalysisForm
        choices={choices.map((item) => ({
          library_id: item.library_id,
          display_name: item.display_name,
          category: item.category,
          availability: item.availability,
        }))}
        selectedId={selectedId}
        limits={limits}
        scanning={scanning}
        canStart={library !== null && selectedId !== null && !dedup.pending}
        onSelect={onSelectLibrary}
        onStart={onStart}
      />
      {libraries.state.next_cursor && (
        <button className="secondary" disabled={libraries.state.loadingMore} onClick={libraries.loadMore}>
          载入更多资源库
        </button>
      )}
      {selectedId === null ? (
        <EmptyState title="选择要查重的资源库">
          精确查重只在后台比较完整强哈希，不会移动、复制或删除任何文件。选择资源库后可以查看预算并开始分析。
        </EmptyState>
      ) : libraryError !== null ? (
        <ErrorState message={libraryError} retry={retryLibrary} />
      ) : library === null ? (
        <LoadingState label="正在读取资源库" />
      ) : (
        <DedupReport
          dedup={dedup}
          section={section}
          recheck={recheck}
          exported={exported}
          onSelectSection={onSelectSection}
        />
      )}
    </section>
  );
}

/**
 * Everything that describes the analysis of the selected library. It renders the job's own state first
 * and the findings only once the server says a report exists, so a still-running or discarded analysis
 * can never be shown as an empty result.
 */
function DedupReport({
  dedup,
  section,
  recheck,
  exported,
  onSelectSection,
}: {
  dedup: DedupJobHandle;
  section: DedupSection;
  recheck: DedupRecheck | null;
  exported: DedupExportReceipt | null;
  onSelectSection: (section: DedupSection) => void;
}) {
  return (
    <>
      {dedup.message !== null && <ErrorState message={dedup.message} />}
      {dedup.status === "loading" && dedup.job === null && <LoadingState label="正在读取查重任务" />}
      {dedup.job !== null && (
        <DedupJobStatus job={dedup.job} page={dedup.page} pending={dedup.pending} onCancel={dedup.cancel} />
      )}
      {dedup.status === "idle" && dedup.job === null && (
        <p className="dedup-note" role="status">
          尚未开始分析。点击“开始分析”后，任务在后台运行，关闭页面不会取消任务。
        </p>
      )}
      {dedup.job !== null && dedup.job.report_available && dedup.page === null && (
        <LoadingState label="正在读取分析结果" />
      )}
      {dedup.job !== null && !dedup.job.report_available && dedup.job.state === "succeeded" && (
        <EmptyState title="本次结果已不可读取">
          服务器只保留最近若干份结果。请重新开始分析；旧结果不会以空结果的形式展示。
        </EmptyState>
      )}
      {dedup.job !== null && !dedup.job.report_available && dedup.job.state !== "succeeded" && (
        <p className="dedup-note" role="status">
          本次分析没有可读取的结果。原因见上方任务状态；失败或取消都不会改动任何文件。
        </p>
      )}
      {dedup.page !== null && <DedupResults dedup={dedup} section={section} onSelectSection={onSelectSection} />}
      {dedup.page !== null && <DedupFollowUp dedup={dedup} recheck={recheck} exported={exported} />}
    </>
  );
}

function DedupHeading() {
  return (
    <div className="page-heading">
      <div>
        <span className="eyebrow">资源库管理</span>
        <h1>精确查重</h1>
        <p>
          只按完整强哈希比较已登记资源库中的文件，只读分析，不会移动、复制或删除任何文件。本页只分析所选资源库，结果不代表其他资源库。
        </p>
      </div>
    </div>
  );
}

function DedupResults({
  dedup,
  section,
  onSelectSection,
}: {
  dedup: DedupJobHandle;
  section: DedupSection;
  onSelectSection: (section: DedupSection) => void;
}) {
  const summary = dedup.page!.summary;
  const active = dedupKinds.find((item) => sectionOf(item.kind) === section) ?? dedupKinds[0]!;
  const opened = dedup.group !== null;
  const lastOpened = useRef<string | null>(null);
  useEffect(() => {
    // Closing the detail returns focus to the row that opened it. The row is still in place because the
    // detail is rendered inside the list rather than over it.
    if (opened) {
      lastOpened.current = dedup.group?.group_key ?? null;
      return;
    }
    const key = lastOpened.current;
    lastOpened.current = null;
    if (key !== null) window.document.querySelector<HTMLElement>(`[data-dedup-group="${CSS.escape(key)}"]`)?.focus();
  }, [opened, dedup.group]);

  return (
    <>
      <DedupStatistics summary={summary} />
      <div className="dedup-sections" role="tablist" aria-label="结果区块">
        {dedupKinds.map((item) => (
          <button
            key={item.kind}
            type="button"
            role="tab"
            aria-selected={active.kind === item.kind}
            className={active.kind === item.kind ? "is-current" : ""}
            onClick={() => onSelectSection(sectionOf(item.kind))}
          >
            {item.label}
            <span>{countOf(summary, item.kind).toLocaleString("zh-CN")}</span>
          </button>
        ))}
      </div>
      <div className="dedup-split">
        <DedupResultList
          groups={dedup.page!.groups}
          items={dedup.page!.items}
          kindLabel={active.label}
          kindNote={active.note}
          total={dedup.page!.total}
          offset={dedup.page!.offset}
          nextCursor={dedup.page!.next_cursor}
          truncated={summary.truncated}
          selectedGroup={dedup.group?.group_key ?? null}
          loadingMore={dedup.loadingMore}
          busy={dedup.pending || dedup.status === "loading"}
          onOpenGroup={dedup.openGroup}
          onMore={dedup.more}
        />
        {opened && <DedupGroupDetail group={dedup.group!} loading={dedup.pending} close={dedup.closeGroup} />}
      </div>
    </>
  );
}

function sectionOf(kind: (typeof dedupKinds)[number]["kind"]): DedupSection {
  return kind === "ByteDuplicateGroup" ? "groups" : kind === "Unverified" ? "unverified" : "unreadable";
}

function countOf(summary: DedupSummary, kind: (typeof dedupKinds)[number]["kind"]): number {
  if (kind === "ByteDuplicateGroup") return summary.duplicate_group_count;
  return kind === "Unverified" ? summary.unverified_count : summary.unreadable_count;
}

/**
 * What the analysis found, as separate facts. Duplicate groups are never mixed with the counts of files
 * this run could not verify, because the second number is not a subset of the first.
 */
function DedupStatistics({ summary }: { summary: DedupSummary }) {
  return (
    <section className="dedup-statistics" aria-labelledby="dedup-statistics-heading">
      <h2 id="dedup-statistics-heading" className="dedup-section-heading">
        分析结果
      </h2>
      <p className="dedup-note">
        结果版本 {summary.analysis_version} · 分析时间 {formatDate(summary.analyzed_at)}
      </p>
      <dl className="dedup-facts">
        <div>
          <dt>重复组</dt>
          <dd>{summary.duplicate_group_count.toLocaleString("zh-CN")}</dd>
        </div>
        <div>
          <dt>重复文件数</dt>
          <dd>{summary.duplicate_file_count.toLocaleString("zh-CN")}</dd>
        </div>
        <div>
          <dt>重复内容合计</dt>
          <dd>{formatBytes(String(summary.statistics.byte_duplicate_bytes))}（仅供了解规模，不代表建议删除）</dd>
        </div>
        <div>
          <dt>未验证内容</dt>
          <dd>{summary.unverified_count.toLocaleString("zh-CN")}</dd>
        </div>
        <div>
          <dt>本次不可读</dt>
          <dd>{summary.unreadable_count.toLocaleString("zh-CN")}</dd>
        </div>
        <div>
          <dt>保留条目</dt>
          <dd>{summary.retained_item_count.toLocaleString("zh-CN")}</dd>
        </div>
      </dl>
      <p className="dedup-note">
        计划摘要 {summary.plan_digest.slice(0, 16)}… · {summary.retention_notice}
      </p>
    </section>
  );
}

/**
 * The two closing operations. Both are bound to the version on screen: recheck answers about the plan
 * the reader is looking at, and the export writes that same version rather than the newest one.
 */
function DedupFollowUp({
  dedup,
  recheck,
  exported,
}: {
  dedup: DedupJobHandle;
  recheck: DedupRecheck | null;
  exported: DedupExportReceipt | null;
}) {
  const version = dedup.page!.summary.analysis_version;
  return (
    <section className="dedup-followup" aria-labelledby="dedup-followup-heading">
      <h2 id="dedup-followup-heading" className="dedup-section-heading">
        重新核对与导出
      </h2>
      <div className="dedup-actions">
        <button type="button" className="secondary" onClick={dedup.runRecheck} disabled={dedup.pending}>
          重新核对
        </button>
        <button type="button" className="primary" onClick={dedup.exportPlan} disabled={dedup.pending}>
          导出计划
        </button>
      </div>
      <p className="dedup-note">
        两个操作都绑定结果版本 {version}。重新核对只读取本次分析记录的来源；导出的是一份计划文档，不会执行任何文件操作。
      </p>
      {exported !== null && (
        <p className="dedup-note" role="status">
          已导出结果版本 {exported.analysis_version} 的计划文档，文件名 {exported.file_name}。
        </p>
      )}
      {recheck !== null && <RecheckNotice recheck={recheck} />}
    </section>
  );
}

/**
 * The outcome of a recheck. When the plan on screen is no longer current the page says so and asks for a
 * new analysis instead of exporting what it is showing.
 */
function RecheckNotice({ recheck }: { recheck: DedupRecheck }) {
  return (
    <div className={recheck.plan_still_current ? "dedup-complete" : "dedup-warning"} role="status">
      <h3>核对结果</h3>
      <p>{recheck.status_text}</p>
      <p>
        变化 {recheck.changed_paths.length} 项 · 消失 {recheck.disappeared_paths.length} 项 · 新增{" "}
        {recheck.new_paths.length} 项
      </p>
      {recheck.reasons.length > 0 && <p>{recheck.reasons.join("；")}</p>}
      {!recheck.plan_still_current && (
        <p>本次核对的结果与当前展示的计划不一致，请重新分析后再导出，避免导出过期计划。</p>
      )}
      {!recheck.report_available && <p>{recheck.retention_notice}</p>}
    </div>
  );
}
