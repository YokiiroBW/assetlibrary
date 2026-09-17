using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Rebuilds a retained report into the module's own plan shape, so a recheck runs the published
/// <see cref="DedupAnalyzer.RecountAsync"/> comparison instead of a second, weaker one.
/// </summary>
public static class DedupReportRehydrator
{
    public static DedupCurationPlan ToPlan(DedupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var items = new List<DedupPlanItem>(
            report.DuplicateItemCount + report.Unverified.Count + report.Unreadable.Count);
        var groups = new List<DedupPlanGroup>(report.Groups.Count);
        foreach (var group in report.Groups)
        {
            groups.Add(new DedupPlanGroup(
                group.GroupKey,
                group.Length,
                [.. group.Members.Select(DedupFileIdentity.Of)],
                AssetRelation.ByteDuplicate,
                group.IdentityMergeProposed));
            items.AddRange(group.Members.Select(DedupReportItemMapper.ToPlanItem));
        }

        items.AddRange(report.Unverified.Select(DedupReportItemMapper.ToPlanItem));
        items.AddRange(report.Unreadable.Select(DedupReportItemMapper.ToPlanItem));
        return new DedupCurationPlan(
            report.AnalysisId,
            report.AnalyzedAt,
            report.PolicyVersion,
            report.AcceptedSources,
            report.RejectedSources,
            items,
            groups,
            report.Statistics,
            report.Summary,
            report.PlanDigest);
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
