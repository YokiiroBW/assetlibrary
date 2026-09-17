using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Places the findings of one analyzed plan into the sections the workbench shows, and beside them the
/// complete bounded comparison facts a recheck needs. The two are deliberately different: a section is
/// a question a reader asks ("which files are byte duplicates?"), while the facts are what this run
/// observed about every file it retained. Deriving the facts back out of the sections is impossible —
/// a verified file with no duplicate peer appears in no section, and a display rule may legitimately
/// show one file in more than one — so they are kept instead of reconstructed.
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

        // One fact per file, in the plan's own stable order. A plan cannot name one file twice, and if
        // it ever did, taking the first occurrence keeps one identity to one fact instead of throwing
        // inside a page read.
        var facts = retained
            .DistinctBy(item => item.SourceIdentity, StringComparer.Ordinal)
            .OrderBy(item => item.SourceIdentity, StringComparer.Ordinal)
            .Select(item => DedupReportItemMapper.ToReportItem(item, roots))
            .ToArray();

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

        // A file already shown as a member of a proven duplicate group is not repeated in the sections
        // that describe what could not be proven, so a reader counts each file exactly once.
        var unclaimed = facts
            .Where(item => !claimed.Contains(DedupFileIdentity.Of(item)))
            .ToArray();
        return new DedupReportProjection(
            duplicates,
            [.. unclaimed.Where(item => DedupFindingClassifier.Section(item) == DedupFindingKind.Unverified)],
            [.. unclaimed.Where(item => DedupFindingClassifier.Section(item) == DedupFindingKind.Unreadable)],
            facts,
            truncated);
    }
}

/// <summary>
/// The evidence one report exposes: complete bounded facts, the display sections derived from them,
/// and whether the run had to leave items out. <paramref name="Facts"/> is the only list a recheck may
/// rebuild a plan from.
/// </summary>
internal sealed record DedupReportProjection(
    IReadOnlyList<DedupReportGroup> Duplicates,
    IReadOnlyList<DedupReportItem> Unverified,
    IReadOnlyList<DedupReportItem> Unreadable,
    IReadOnlyList<DedupReportItem> Facts,
    bool Truncated);
