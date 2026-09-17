using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Rebuilds a retained report into the module's own plan shape, so a recheck runs the published
/// <see cref="DedupAnalyzer.RecountAsync"/> comparison instead of a second, weaker one. It rebuilds
/// from the report's complete bounded facts, never from its display sections: a section is a question
/// a reader asked, and the files that answer no question are exactly the evidence a recheck needs.
/// </summary>
public static class DedupReportRehydrator
{
    public static DedupCurationPlan ToPlan(DedupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var items = new List<DedupPlanItem>(report.Facts.Count);
        foreach (var fact in report.Facts)
        {
            items.Add(DedupReportItemMapper.ToPlanItem(fact));
        }

        var groups = new List<DedupPlanGroup>(report.Groups.Count);
        foreach (var group in report.Groups)
        {
            groups.Add(new DedupPlanGroup(
                group.GroupKey,
                group.Length,
                [.. group.Members.Select(DedupFileIdentity.Of)],
                AssetRelation.ByteDuplicate,
                group.IdentityMergeProposed));
        }

        return new DedupCurationPlan(
            report.AnalysisId,
            report.AnalyzedAt,
            report.PolicyVersion,
            report.AcceptedSources,
            report.RejectedSources,
            items,
            [.. groups.OrderBy(group => group.GroupKey, StringComparer.Ordinal)],
            report.Statistics,
            report.Summary,
            report.PlanDigest);
    }

    /// <summary>
    /// Rebuilds the request a recheck needs. The roots come from the report's own accepted sources, so
    /// a recheck reads exactly the directories the original analysis was allowed to read instead of
    /// whatever a caller submits now. The report's truncation is carried across, because a comparison
    /// against part of a run must state that limit instead of implying the whole run still holds.
    /// </summary>
    public static DedupRecountRequest Recount(DedupReport report, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new DedupRecountRequest(
            ToPlan(report),
            SourcesOf(report),
            timeout,
            report.Truncated);
    }

    /// <summary>
    /// Rebuilds the request a recheck needs. The roots come from the report's own accepted sources, so
    /// a recheck reads exactly the directories the original analysis was allowed to read instead of
    /// whatever a caller submits now.
    /// </summary>
    public static IReadOnlyList<DedupSourceRequest> SourcesOf(DedupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sources = new List<DedupSourceRequest>(report.AcceptedSources.Count);
        for (var index = 0; index < report.AcceptedSources.Count; index++)
        {
            if (index >= report.AcceptedLibraryIds.Count
                || !Guid.TryParseExact(report.AcceptedLibraryIds[index], "D", out var libraryId)
                || libraryId == Guid.Empty)
            {
                // A source whose library cannot be recovered must not be read under a guessed id: the
                // remaining sources are returned and the caller refuses an empty scope explicitly.
                break;
            }

            var accepted = report.AcceptedSources[index];
            sources.Add(new DedupSourceRequest(
                accepted.SourceId,
                accepted.DisplayName,
                accepted.Role,
                new LibraryId(libraryId),
                new CanonicalLibraryRoot(accepted.Root, RootPathComparison.CaseInsensitive)));
        }

        return sources;
    }
}
