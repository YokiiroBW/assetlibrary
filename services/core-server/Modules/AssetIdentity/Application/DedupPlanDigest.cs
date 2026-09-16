using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Canonical content digest of a curation preview. It covers exactly what this run observed about
/// the submitted sources, so two previews of unchanged sources are equal and a preview of changed
/// sources is not. Per-run identities (the analysis id, the library ids created for this run) are
/// deliberately excluded: composing them would make every preview unique and the digest useless.
/// </summary>
public static class DedupPlanDigest
{
    public static string Compute(
        DedupAnalysisId analysisId,
        string policyVersion,
        IReadOnlyList<DedupAcceptedSource> acceptedSources,
        IReadOnlyList<DedupRejectedSource> rejectedSources,
        IReadOnlyList<DedupPlanItem> items,
        IReadOnlyList<DedupPlanGroup> groups,
        DedupPlanStatistics statistics,
        IReadOnlyList<DedupSourceFailure> sourceFailures)
    {
        ArgumentNullException.ThrowIfNull(acceptedSources);
        ArgumentNullException.ThrowIfNull(rejectedSources);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(sourceFailures);
        _ = analysisId;

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalFor(
            policyVersion,
            acceptedSources,
            rejectedSources,
            items,
            groups,
            statistics,
            sourceFailures))));
    }

    private static string CanonicalFor(
        string policyVersion,
        IReadOnlyList<DedupAcceptedSource> acceptedSources,
        IReadOnlyList<DedupRejectedSource> rejectedSources,
        IReadOnlyList<DedupPlanItem> items,
        IReadOnlyList<DedupPlanGroup> groups,
        DedupPlanStatistics statistics,
        IReadOnlyList<DedupSourceFailure> sourceFailures)
    {
        var canonical = new StringBuilder();
        Append(canonical, "policy", policyVersion);
        Append(canonical, "counts", string.Join(
            '|',
            statistics.ObservedEntries,
            statistics.AnalyzedFiles,
            statistics.NotReadFiles,
            statistics.FailedFiles,
            statistics.SkippedFiles,
            statistics.ByteDuplicateGroups,
            statistics.ByteDuplicateFiles));

        foreach (var source in acceptedSources.OrderBy(candidate => candidate.Root, StringComparer.Ordinal))
        {
            Append(canonical, "source", string.Join(
                '|',
                source.SourceId.Value.ToString("N", CultureInfo.InvariantCulture),
                source.Role.ToString(),
                source.Root));
        }

        foreach (var rejected in rejectedSources.OrderBy(candidate => candidate.Root, StringComparer.Ordinal))
        {
            Append(canonical, "rejected", string.Join(
                '|',
                rejected.SourceId.Value.ToString("N", CultureInfo.InvariantCulture),
                rejected.Rejection.ToString(),
                rejected.Root));
        }

        foreach (var failure in sourceFailures.OrderBy(candidate => candidate.SourceId.Value))
        {
            Append(canonical, "source-failure", string.Join(
                '|',
                failure.SourceId.Value.ToString("N", CultureInfo.InvariantCulture),
                failure.ReasonCode));
        }

        foreach (var item in items.OrderBy(candidate => candidate.SourceIdentity, StringComparer.Ordinal))
        {
            Append(canonical, "item", string.Join(
                '|',
                item.SourceIdentity,
                item.State.ToString(),
                item.ReadState.ToString(),
                item.Failure.ToString(),
                item.SkipReason.ToString(),
                item.Category.ToString(),
                item.GroupKey ?? string.Empty,
                string.Join(',', item.Relations.OrderBy(value => value).Select(value => value.ToString()))));
        }

        foreach (var group in groups.OrderBy(candidate => candidate.GroupKey, StringComparer.Ordinal))
        {
            Append(canonical, "group", string.Join(
                '|',
                group.GroupKey,
                group.Evidence.ToString(),
                group.IdentityMergeProposed.ToString(),
                string.Join(',', group.MemberIdentities.Order(StringComparer.Ordinal))));
        }

        return canonical.ToString();
    }

    private static void Append(StringBuilder canonical, string section, string payload) =>
        canonical.Append(section).Append('\u001f').Append(payload).Append('\u001e');
}
