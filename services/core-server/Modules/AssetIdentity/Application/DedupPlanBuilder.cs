using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Domain;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Turns resolved observations into the serializable preview. It is separated from the analyzer so
/// read orchestration stays independent of the reporting shape, and it computes only derived
/// numbers: no method here can add, remove or rewrite a source file.
/// </summary>
internal sealed class DedupPlanBuilder(IDedupClock clock)
{
    public DedupCurationPlan Build(
        DedupAnalysisRequest request,
        DedupAnalysisScope scope,
        IReadOnlyList<DedupAnalysisEntry> entries,
        IReadOnlyList<DedupPlanGroup> groups,
        IReadOnlyDictionary<string, IReadOnlyList<AssetRelation>> relations,
        IReadOnlyList<DedupSourceFailure> failures,
        bool scanBoundsReached,
        DedupAnalysisStatus status)
    {
        var items = DedupPlanItemFactory.Create(entries, groups, relations);
        var orderedGroups = Order(groups);
        var statistics = DedupPlanItemFactory.Summarize(items, orderedGroups);
        var summary = DedupPlanItemFactory.Summary(items, failures, scanBoundsReached, status);
        var accepted = Order(scope.Accepted);
        var rejected = DedupPlanSourceFactory.Rejections(scope);
        return new DedupCurationPlan(
            request.AnalysisId,
            clock.UtcNow,
            DedupContractText.PolicyVersion,
            accepted,
            rejected,
            items,
            orderedGroups,
            statistics,
            summary,
            DedupPlanDigest.Compute(
                request.AnalysisId,
                DedupContractText.PolicyVersion,
                accepted,
                rejected,
                items,
                orderedGroups,
                statistics,
                summary.SourceFailures));
    }

    private static IReadOnlyList<DedupAcceptedSource> Order(IReadOnlyList<DedupSourceRequest> sources) =>
    [
        .. sources
            .Select(source => new DedupAcceptedSource(
                source.SourceId,
                source.DisplayName,
                source.Role,
                source.Root.Value))
            .OrderBy(source => source.Root, StringComparer.Ordinal),
    ];

    private static IReadOnlyList<DedupPlanGroup> Order(IReadOnlyList<DedupPlanGroup> groups) =>
        [.. groups.OrderBy(group => group.GroupKey, StringComparer.Ordinal)];
}
