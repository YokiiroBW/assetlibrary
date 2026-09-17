using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Places the findings of one analyzed plan into the three reviewable sections the workbench shows.
/// The order is fixed and total, because a cursor is only meaningful while the same question returns
/// the same sequence: content that was never read is never folded into the duplicate section, and a
/// file with no same-length peer is never presented as verified content.
/// </summary>
internal static class DedupFindingProjection
{
    public static DedupReportProjection Project(
        DedupCurationPlan plan,
        IReadOnlyDictionary<string, string> roots,
        int maximumItems)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
        var truncated = plan.Items.Count > maximumItems;
        var retained = truncated ? plan.Items.Take(maximumItems).ToArray() : [.. plan.Items];
        var byIdentity = retained.ToDictionary(item => item.SourceIdentity, StringComparer.Ordinal);
        var duplicates = new List<DedupReportGroup>(plan.Groups.Count);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        // A group may name observations this run retained differently; only members that are actually
        // present are listed, so the report never shows a file it cannot describe.
        foreach (var group in plan.Groups
                     .OrderBy(candidate => candidate.Length)
                     .ThenBy(candidate => candidate.GroupKey, StringComparer.Ordinal))
        {
            var members = group.MemberIdentities
                .Where(byIdentity.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(identity => byIdentity[identity])
                .ToArray();
            if (members.Length < 2)
            {
                continue;
            }

            foreach (var identity in members.Select(DedupFileIdentity.Of))
            {
                claimed.Add(identity);
            }

            duplicates.Add(new DedupReportGroup(
                group.GroupKey,
                group.Length,
                DedupFileIdentity.EvidenceHash(group.GroupKey),
                group.IdentityMergeProposed,
                [.. members.Select(member => DedupReportItemMapper.ToReportItem(member, roots))]));
        }

        // Everything with a proven-hash peer is already in a group; what remains keeps the same
        // ordering basis, so one report has one stable sequence a cursor can advance through.
        var remainder = retained
            .Where(item => !claimed.Contains(item.SourceIdentity))
            .OrderBy(item => item.SourceIdentity, StringComparer.Ordinal)
            .Select(item => DedupReportItemMapper.ToReportItem(item, roots))
            .ToArray();
        return new DedupReportProjection(
            duplicates,
            [.. remainder.Where(DedupFindingClassifier.IsUnverified)],
            [.. remainder.Where(DedupFindingClassifier.IsUnreadable)],
            truncated);
    }
}

/// <summary>The three sections a report exposes, plus whether the run had to leave items out.</summary>
internal sealed record DedupReportProjection(
    IReadOnlyList<DedupReportGroup> Duplicates,
    IReadOnlyList<DedupReportItem> Unverified,
    IReadOnlyList<DedupReportItem> Unreadable,
    bool Truncated);
