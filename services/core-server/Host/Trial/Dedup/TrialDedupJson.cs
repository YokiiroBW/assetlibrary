using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;

namespace AssetLibrary.CoreServer.Hosting.Trial;

/// <summary>
/// Shared wire vocabulary of the dedup workbench. Every fact is stated explicitly — including which
/// report version a value belongs to — so the page never infers freshness, and an empty section can
/// never be read as "no duplicates found".
/// </summary>
internal static class TrialDedupJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Serialize(JsonObject body) => body.ToJsonString(Options);

    public static JsonObject Job(DedupJobView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new JsonObject
        {
            ["task_id"] = view.TaskId.ToString("D"),
            ["library_id"] = view.LibraryId.Value.ToString("D"),
            ["library_display_name"] = view.LibraryDisplayName,
            ["state"] = view.State,
            ["cancellation_requested"] = view.CancellationRequested,
            ["can_cancel"] = view.CanCancel,
            ["can_retry"] = view.CanRetry,
            ["report_available"] = view.ReportAvailable,
            ["analysis_version"] = view.AnalysisVersion,
            ["created_at"] = view.CreatedAt,
            ["updated_at"] = view.UpdatedAt,
            ["failure_code"] = view.FailureCode,
            ["retention_notice"] = view.RetentionBoundary,
            ["read_only_notice"] = DedupJobContractText.ReadOnlyBoundary,
        };
    }

    public static JsonObject Error(int status, string code) => new()
    {
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = Message(code),
        },
        ["status"] = status,
    };

    /// <summary>
    /// The status a code maps to. A stale plan and a conflicting operation are refusals the caller can
    /// act on, so they are 409 rather than a generic failure; an unreachable store is 503, never 404.
    /// </summary>
    public static int Status(string code) => code switch
    {
        "invalid_request" or "invalid_cursor" or "dedup_payload_invalid" or "dedup_group_not_found" => 400,
        "permission_denied" or "dedup_library_not_allowed" or "library_not_allowed" => 403,
        "dedup_not_found" or "dedup_report_not_retained" or "dedup_task_missing" => 404,
        "idempotency_conflict" or "dedup_already_running" or "dedup_version_conflict"
            or "dedup_scope_rejected" or "results_too_large" => 409,
        "service_unavailable" or "storage_unavailable" or "library_not_found" => 503,
        _ => 500,
    };

    /// <summary>
    /// The reviewable facts of one report. Counts, the retention boundary and the analysis version are
    /// separate fields on purpose: a page shows them in separate blocks instead of one number.
    /// </summary>
    public static JsonObject Summary(DedupReportSummary summary) => new()
    {
        ["policy_version"] = summary.PolicyVersion,
        ["analyzed_at"] = summary.AnalyzedAt,
        ["plan_digest"] = summary.PlanDigest,
        ["statistics"] = Statistics(summary.Statistics),
        ["plan"] = Plan(summary.Plan),
        ["duplicate_group_count"] = summary.DuplicateGroupCount,
        ["duplicate_file_count"] = summary.DuplicateFileCount,
        ["unverified_count"] = summary.UnverifiedCount,
        ["unreadable_count"] = summary.UnreadableCount,
        ["unique_count"] = summary.UniqueCount,
        ["retained_item_count"] = summary.RetainedItemCount,
        ["truncated"] = summary.Truncated,
        ["retention_notice"] = summary.RetentionBoundary,
        ["analysis_version"] = summary.ExpectedVersion,
    };

    public static JsonObject Item(DedupReportItem item) => new()
    {
        ["source_id"] = item.SourceId.Value.ToString("D"),
        ["root"] = item.Root,
        ["relative_path"] = item.RelativePath,
        ["name"] = item.RelativePath[(item.RelativePath.LastIndexOf('/') + 1)..],
        ["length"] = item.Length,
        ["sha256"] = item.Sha256,
        ["structure_hash"] = item.StructureHash,
        ["last_write_time_utc"] = item.LastWriteTimeUtc,
        ["state"] = item.State.ToString(),
        ["state_text"] = DedupText.Describe(item.State),
        ["read_state"] = item.ReadState.ToString(),
        ["read_state_text"] = DedupText.Describe(item.ReadState),
        ["failure"] = item.Failure.ToString(),
        ["skip_reason"] = item.SkipReason.ToString(),
        ["group_key"] = item.GroupKey,
        ["category"] = item.Category.ToString(),
        ["relations"] = new JsonArray([.. item.Relations.Select(relation => JsonValue.Create(relation.ToString()))]),
        ["relation_notes"] = new JsonArray([.. item.Relations.Select(relation => JsonValue.Create(DedupText.Describe(relation)))]),
    };

    public static JsonObject Group(DedupReportGroup group) => new()
    {
        ["group_key"] = group.GroupKey,
        ["length"] = group.Length,
        ["evidence_hash"] = group.EvidenceHash,
        ["member_count"] = group.Members.Count,
        ["identity_merge_proposed"] = group.IdentityMergeProposed,
        ["members"] = new JsonArray([.. group.Members.Select(Item)]),
    };

    public static JsonObject Statistics(DedupPlanStatistics statistics) => new()
    {
        ["observed_entries"] = statistics.ObservedEntries,
        ["analyzed_files"] = statistics.AnalyzedFiles,
        ["not_read_files"] = statistics.NotReadFiles,
        ["failed_files"] = statistics.FailedFiles,
        ["skipped_files"] = statistics.SkippedFiles,
        ["byte_duplicate_groups"] = statistics.ByteDuplicateGroups,
        ["byte_duplicate_files"] = statistics.ByteDuplicateFiles,
        ["byte_duplicate_bytes"] = statistics.ByteDuplicateBytes,
        ["read_bytes"] = statistics.ReadBytes,
        ["additional_read_attempts"] = statistics.AdditionalReadAttempts,
    };

    public static JsonObject Plan(DedupPlanSummary plan) => new()
    {
        ["status"] = plan.Status.ToString(),
        ["status_text"] = DedupText.Describe(plan.Status),
        ["unreadable_paths"] = new JsonArray([.. plan.UnreadablePaths.Select(path => JsonValue.Create(path))]),
        ["incomplete_reason_count"] = plan.IncompleteReasons.Count,
        ["scan_bounds_reached"] = plan.ScanBoundsReached,
        ["failure_code"] = plan.FailureCode,
        ["source_failures"] = new JsonArray([.. plan.SourceFailures.Select(failure => (JsonNode)new JsonObject
        {
            ["source_id"] = failure.SourceId.Value.ToString("D"),
            ["reason_code"] = failure.ReasonCode,
        })]),
    };

    private static string Message(string code) => code switch
    {
        "dedup_not_found" => "没有找到对应的查重任务。",
        "dedup_report_not_retained" => "该任务当前没有可读取的分析结果，请重新分析。",
        "dedup_already_running" => "该资产库已有正在进行的查重任务。",
        "dedup_version_conflict" => "请求绑定的结果版本与当前版本不一致，请重新读取结果。",
        "dedup_scope_rejected" => "该资产库当前不可作为查重来源。",
        "idempotency_conflict" => "请求与已有任务或当前计划不一致。",
        "invalid_cursor" => "结果游标无效或已过期，请重新读取结果。",
        "permission_denied" => "当前请求没有操作权限。",
        "service_unavailable" => "服务或存储暂时不可用，请稍后重试。",
        "storage_unavailable" => "存储暂时不可用，请稍后重试。",
        "results_too_large" => "结果集超过可导出的上限。",
        _ => "请求未被执行。",
    };
}
