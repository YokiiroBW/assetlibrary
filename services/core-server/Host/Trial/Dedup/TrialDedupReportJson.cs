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

    public static JsonObject Recheck(DedupRecheckView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new JsonObject
        {
            ["task_id"] = view.TaskId.ToString("D"),
            ["status"] = view.Status.ToString(),
            ["status_text"] = DedupText.Describe(view.Status),
            ["plan_still_current"] = view.PlanStillCurrent,
            ["reasons"] = new JsonArray([.. view.Reasons.Select(reason => JsonValue.Create(reason))]),
            ["changed_paths"] = new JsonArray([.. view.ChangedPaths.Select(path => JsonValue.Create(path))]),
            ["disappeared_paths"] = new JsonArray([.. view.DisappearedPaths.Select(path => JsonValue.Create(path))]),
            ["new_paths"] = new JsonArray([.. view.NewPaths.Select(path => JsonValue.Create(path))]),
            ["plan_digest"] = view.PlanDigest,
            ["previous_plan_digest"] = view.PreviousPlanDigest,
            ["analysis_version"] = view.AnalysisVersion,
            ["report_available"] = view.ReportAvailable,
            ["retention_notice"] = DedupJobContractText.RetentionBoundary,
        };
    }
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
            ["limits"] = new JsonObject
            {
                ["maximum_files"] = document.Limits.MaximumFiles,
                ["maximum_bytes"] = document.Limits.MaximumBytes,
                ["maximum_file_bytes"] = document.Limits.MaximumFileBytes,
                ["hash_concurrency"] = document.Limits.HashConcurrency,
            },
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
