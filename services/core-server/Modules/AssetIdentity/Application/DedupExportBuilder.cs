using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Assembles the exportable plan document. It is a plan only: it carries the source version, the
/// latest recheck evidence and the ceilings the run executed under. It contains no move, copy,
/// rename or delete instruction and no confirmation token a writer could consume.
/// </summary>
internal sealed class DedupExportBuilder(DedupReportRegistry reports, TimeProvider timeProvider)
{
    public const int FormatVersion = 1;
    public const string DocumentType = "assetlibrary.dedup.plan";

    public DedupExportDocument Build(Guid taskId, DedupReportKey key, DedupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new DedupExportDocument(
            FormatVersion,
            DocumentType,
            taskId.ToString("D"),
            key.VersionText,
            report.LibraryId.Value.ToString("D"),
            report.LibraryDisplayName,
            report.PolicyVersion,
            report.AnalyzedAt,
            timeProvider.GetUtcNow(),
            report.PlanDigest,
            report.RetentionBoundary,
            GrantsFileOperation: false,
            new DedupAnalysisLimitsSnapshot(
                report.Limits.MaximumFiles,
                report.Limits.MaximumBytes,
                report.Limits.MaximumFileBytes,
                report.Limits.HashConcurrency),
            report.AcceptedSources,
            report.RejectedSources,
            report.Statistics,
            report.Summary,
            Evidence(key),
            [.. report.Groups.Select(Group)],
            report.Unverified,
            report.Unreadable,
            report.Truncated);
    }

    /// <summary>
    /// The recheck evidence that ships with the plan. A version that was never rechecked says so
    /// explicitly: an absent field would read as "verified", which is the opposite of the truth.
    /// </summary>
    public DedupRecheckEvidence Evidence(DedupReportKey key) =>
        reports.EvidenceOf(key) ?? new DedupRecheckEvidence(
            Performed: false,
            Status: "not_revalidated",
            Reasons: [],
            ChangedCount: 0,
            DisappearedCount: 0,
            NewCount: 0,
            PerformedAt: null,
            PlanDigest: string.Empty);

    private static DedupExportGroup Group(DedupReportGroup group) => new(
        group.GroupKey,
        group.Length,
        group.EvidenceHash,
        "complete_strong_hash",
        [.. group.Members.Select(Member)]);

    private static DedupExportMember Member(DedupReportItem item) => new(
        item.RelativePath,
        item.Root,
        item.Length,
        item.Sha256,
        item.StructureHash,
        item.LastWriteTimeUtc,
        DedupText.Describe(item.ReadState),
        DedupText.Describe(AssetRelation.ByteDuplicate));
}
