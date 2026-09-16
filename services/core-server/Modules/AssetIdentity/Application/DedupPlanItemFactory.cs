using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Domain;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Builds the item rows of a preview. An item states what was observed about one file and nothing
/// about what should happen to it.
/// </summary>
internal static class DedupPlanItemFactory
{
    public static IReadOnlyList<DedupPlanItem> Create(
        IReadOnlyList<DedupAnalysisEntry> entries,
        IReadOnlyList<DedupPlanGroup> groups,
        IReadOnlyDictionary<string, IReadOnlyList<AssetRelation>> relations)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(relations);
        var groupByMember = GroupIndex(groups);
        var items = new List<DedupPlanItem>(entries.Count);
        foreach (var entry in entries.OrderBy(entry => entry.SourceIdentity, StringComparer.Ordinal))
        {
            var entryRelations = relations.GetValueOrDefault(entry.SourceIdentity);
            var groupKey = groupByMember.GetValueOrDefault(entry.SourceIdentity);
            items.Add(new DedupPlanItem(
                entry.SourceIdentity,
                entry.SourceId,
                entry.Length,
                entry.Signature?.Sha256.Value,
                entry.Signature?.StructureHash ?? 0,
                entry.LastWriteTimeUtc,
                ToPlanState(entry),
                entry.ReadState,
                entry.Failure,
                entry.SkipReason,
                groupKey,
                groupKey is not null
                    ? DedupCategory.ByteDuplicateGroup
                    : DedupAnalysisPolicy.Categorize(entry, entryRelations),
                entryRelations ?? []));
        }

        return items;
    }

    public static DedupPlanStatistics Summarize(
        IReadOnlyList<DedupPlanItem> items,
        IReadOnlyList<DedupPlanGroup> groups)
    {
        var analyzed = items.Count(item => item.ReadState == DedupItemReadState.ContentVerified);
        var failed = items.Count(item => item.ReadState == DedupItemReadState.ReadFailed);
        return new DedupPlanStatistics(
            items.Count,
            analyzed,
            items.Count - analyzed - failed,
            failed,
            items.Count(item => item.ReadState == DedupItemReadState.SkippedByBudget),
            groups.Count,
            groups.Sum(group => group.MemberIdentities.Count),
            groups.Sum(group => group.Length * (group.MemberIdentities.Count - 1)),
            items
                .Where(item => item.ReadState is DedupItemReadState.ContentVerified
                    or DedupItemReadState.ReadFailed)
                .Sum(item => item.Length),
            AdditionalReadAttempts: 0);
    }

    /// <summary>
    /// States the scan boundary honestly. Unreadable paths and an exhausted budget are listed so a
    /// reader cannot mistake an incomplete preview for "everything else is unique".
    /// </summary>
    public static DedupPlanSummary Summary(
        IReadOnlyList<DedupPlanItem> items,
        IReadOnlyList<DedupSourceFailure> failures,
        bool scanBoundsReached,
        DedupAnalysisStatus status)
    {
        var unreadable = new SortedSet<string>(StringComparer.Ordinal);
        var reasons = new SortedSet<DedupReadFailure>();
        var incomplete = new SortedSet<DedupSkipReason>();
        foreach (var item in items)
        {
            if (item.State == DedupPlanItemState.Unreadable)
            {
                unreadable.Add(item.PathText);
                reasons.Add(item.Failure);
            }

            if (item.SkipReason is DedupSkipReason.ExceedsBudget)
            {
                incomplete.Add(item.SkipReason);
            }
        }

        return new DedupPlanSummary(
            status,
            [.. unreadable],
            [.. reasons],
            [.. incomplete],
            scanBoundsReached,
            failures.Count == 0
                ? null
                : failures.OrderBy(failure => failure.SourceId.Value).First().ReasonCode,
            [.. failures
                .OrderBy(failure => failure.SourceId.Value)
                .ThenBy(failure => failure.ReasonCode, StringComparer.Ordinal)]);
    }

    private static Dictionary<string, string> GroupIndex(IReadOnlyList<DedupPlanGroup> groups)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var member in group.MemberIdentities)
            {
                index[member] = group.GroupKey;
            }
        }

        return index;
    }

    private static DedupPlanItemState ToPlanState(DedupAnalysisEntry entry) => entry.ReadState switch
    {
        DedupItemReadState.ContentVerified => DedupPlanItemState.Analyzed,
        DedupItemReadState.ReadFailed => DedupPlanItemState.Unreadable,
        _ => DedupPlanItemState.NotAnalyzed,
    };
}

/// <summary>Records which submitted sources were refused, and why, before any byte was read.</summary>
internal static class DedupPlanSourceFactory
{
    public static IReadOnlyList<DedupRejectedSource> Rejections(DedupAnalysisScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return
        [
            .. scope.Rejected
                .Select(rejected => new DedupRejectedSource(
                    rejected.Source.SourceId,
                    rejected.Rejection,
                    rejected.Source.Root.Value))
                .OrderBy(rejected => rejected.Root, StringComparer.Ordinal),
        ];
    }
}
