using System.Text.Json.Nodes;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;

namespace AssetLibrary.CoreServer.Hosting.Trial;

/// <summary>
/// Wire shape of one page of findings. The version, the total and whether a report exists at all are
/// stated separately from the rows, so a page cannot present an empty section as a finding.
/// </summary>
internal static class TrialDedupPageJson
{
    public static JsonObject Page(DedupResultsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new JsonObject
        {
            ["task_id"] = page.TaskId.ToString("D"),
            ["analysis_version"] = page.AnalysisVersion,
            ["kind"] = page.Kind.ToString(),
            ["kind_text"] = DedupText.Describe(page.Kind),
            ["offset"] = page.Offset,
            ["page_size"] = page.PageSize,
            ["total"] = page.Total,
            ["next_cursor"] = page.NextCursor,
            ["report_available"] = page.ReportAvailable,
            ["groups"] = new JsonArray([.. page.Groups.Select(TrialDedupJson.Group)]),
            ["items"] = new JsonArray([.. page.Items.Select(TrialDedupJson.Item)]),
            ["summary"] = TrialDedupJson.Summary(page.Summary),
        };
    }

    /// <summary>
    /// The answer to a recheck request. A recheck runs on the background path, so this answer exists in
    /// two states: a receipt that names the durable task and the version it will verify, and the outcome
    /// once that task has one. The state is a field rather than something a caller infers from which keys
    /// are present, because a page must never read "not finished yet" as "nothing changed".
    /// </summary>
    public static JsonObject Recheck(DedupRecheckState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var run = state.Run;
        return new JsonObject
        {
            ["state"] = StateName(state.State),
            ["state_text"] = StateText(state.State),
            ["task_id"] = state.ReportTaskId.ToString("D"),
            ["recheck_task_id"] = state.RecheckTaskId.ToString("D"),
            ["completed"] = run?.Completed ?? false,
            ["status"] = run is null ? string.Empty : Recount(run.Status),
            ["status_text"] = run is null ? string.Empty : DedupText.Describe(run.Status),
            ["plan_still_current"] = run?.PlanStillCurrent ?? false,
            ["reasons"] = Paths(run?.Reasons),
            ["changed_paths"] = Paths(run?.ChangedPaths),
            ["disappeared_paths"] = Paths(run?.DisappearedPaths),
            ["new_paths"] = Paths(run?.NewPaths),
            ["plan_digest"] = run?.PlanDigest ?? string.Empty,
            ["previous_plan_digest"] = run?.PreviousPlanDigest,
            ["analysis_version"] = run?.AnalysisVersion ?? string.Empty,
            ["failure_code"] = run?.FailureCode,
            ["report_available"] = run?.Completed ?? false,
            ["retention_notice"] = DedupJobContractText.RetentionBoundary,
            ["read_only_notice"] = DedupJobContractText.ReadOnlyBoundary,
        };
    }

    /// <summary>An absent list is an empty list on the wire, never a missing key a page has to guess at.</summary>
    private static JsonArray Paths(IReadOnlyList<string>? values) =>
        new([.. (values ?? []).Select(value => JsonValue.Create(value))]);

    /// <summary>
    /// The wire names of the recount outcomes, as one table. The module states the same vocabulary for the
    /// evidence it records in an export, and a table here keeps the two spellings comparable by eye rather
    /// than by a second copy of the same branch list.
    /// </summary>
    private static readonly Dictionary<DedupRecountStatus, string> RecountNames = new()
    {
        [DedupRecountStatus.Identical] = "identical",
        [DedupRecountStatus.SourceChanged] = "source_changed",
        [DedupRecountStatus.UnreadableNow] = "unreadable_now",
        [DedupRecountStatus.Disappeared] = "disappeared",
        [DedupRecountStatus.NewContent] = "new_content",
        [DedupRecountStatus.Timeout] = "timeout",
    };

    private static string Recount(DedupRecountStatus status) =>
        RecountNames.TryGetValue(status, out var name)
            ? name
            : throw new ArgumentOutOfRangeException(nameof(status));

    private static string StateName(RecheckRunState state) => state switch
    {
        RecheckRunState.Pending => "pending",
        RecheckRunState.Completed => "completed",
        RecheckRunState.Refused => "refused",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string StateText(RecheckRunState state) => state switch
    {
        RecheckRunState.Pending => "复核已在后台排队，完成后结果才会显示。",
        RecheckRunState.Completed => "复核已完成。",
        RecheckRunState.Refused => "复核未执行，报告版本已不再保留或被取代。",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}

/// <summary>
/// Wire shape of the exported plan. It states the read-only boundary and the file-operation flag in
/// the document itself, so an exported file cannot be mistaken for an execution instruction.
/// </summary>
internal static class TrialDedupExportJson
{
    public static JsonObject Export(DedupExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new JsonObject
        {
            ["format_version"] = document.FormatVersion,
            ["document_type"] = document.DocumentType,
            ["task_id"] = document.TaskId,
            ["analysis_version"] = document.AnalysisVersion,
            ["library_id"] = document.LibraryId,
            ["library_display_name"] = document.LibraryDisplayName,
            ["policy_version"] = document.PolicyVersion,
            ["analyzed_at"] = document.AnalyzedAt,
            ["exported_at"] = document.ExportedAt,
            ["plan_digest"] = document.PlanDigest,
            ["retention_notice"] = document.RetentionBoundary,
            ["grants_file_operation"] = document.GrantsFileOperation,
            ["read_only_notice"] = DedupJobContractText.ReadOnlyBoundary,
            ["limits"] = TrialDedupJson.Limits(document.Limits),
            ["sources"] = new JsonArray([.. document.Sources.Select(Source)]),
            ["rejected_sources"] = new JsonArray([.. document.RejectedSources.Select(Rejected)]),
            ["statistics"] = TrialDedupJson.Statistics(document.Statistics),
            ["plan"] = TrialDedupJson.Plan(document.Summary),
            ["recheck"] = new JsonObject
            {
                ["performed"] = document.Recheck.Performed,
                ["status"] = document.Recheck.Status,
                ["reasons"] = new JsonArray([.. document.Recheck.Reasons.Select(reason => JsonValue.Create(reason))]),
                ["changed_count"] = document.Recheck.ChangedCount,
                ["disappeared_count"] = document.Recheck.DisappearedCount,
                ["new_count"] = document.Recheck.NewCount,
                ["performed_at"] = document.Recheck.PerformedAt,
                ["plan_digest"] = document.Recheck.PlanDigest,
            },
            ["groups"] = new JsonArray([.. document.Groups.Select(Group)]),
            ["unverified"] = new JsonArray([.. document.Unverified.Select(TrialDedupJson.Item)]),
            ["unreadable"] = new JsonArray([.. document.Unreadable.Select(TrialDedupJson.Item)]),
            ["truncated"] = document.Truncated,
        };
    }

    private static JsonObject Source(DedupAcceptedSource source) => new()
    {
        ["source_id"] = source.SourceId.Value.ToString("D"),
        ["display_name"] = source.DisplayName,
        ["role"] = source.Role.ToString(),
        ["root"] = source.Root,
    };

    private static JsonObject Rejected(DedupRejectedSource source) => new()
    {
        ["source_id"] = source.SourceId.Value.ToString("D"),
        ["rejection"] = source.Rejection.ToString(),
        ["root"] = source.Root,
    };

    private static JsonObject Group(DedupExportGroup group) => new()
    {
        ["group_key"] = group.GroupKey,
        ["length"] = group.Length,
        ["evidence_hash"] = group.EvidenceHash,
        ["evidence_basis"] = group.EvidenceBasis,
        ["members"] = new JsonArray([.. group.Members.Select(Member)]),
    };

    private static JsonObject Member(DedupExportMember member) => new()
    {
        ["relative_path"] = member.RelativePath,
        ["root"] = member.Root,
        ["length"] = member.Length,
        ["sha256"] = member.Sha256,
        ["structure_hash"] = member.StructureHash,
        ["last_write_time_utc"] = member.LastWriteTimeUtc,
        ["verification_state"] = member.VerificationState,
        ["relation_note"] = member.RelationNote,
    };
}
