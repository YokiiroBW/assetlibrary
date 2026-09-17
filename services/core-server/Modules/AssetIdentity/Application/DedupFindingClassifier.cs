using System.Security.Cryptography;
using System.Text;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// The single definition of how one file is addressed in this workbench. A plan item, a report item
/// and a recheck all build and compare the identity through this type, so a recheck compares each
/// observation against the record it actually came from.
/// </summary>
internal static class DedupFileIdentity
{
    private const char Separator = '|';

    public static string Of(DedupPlanItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.SourceIdentity;
    }

    public static string Of(DedupReportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Build(item.Root, item.RelativePath);
    }

    public static string Build(string root, string relativePath) =>
        string.Concat(root, Separator.ToString(), relativePath);

    public static (string Root, string RelativePath) Split(string identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var separator = identity.IndexOf(Separator, StringComparison.Ordinal);
        return separator < 0
            ? (string.Empty, identity)
            : (identity[..separator], identity[(separator + 1)..]);
    }

    /// <summary>
    /// The evidence hash a duplicate group points at. It is derived from the group key, which itself is
    /// built from the length and the complete strong hash, so a group whose evidence changed changes
    /// here too.
    /// </summary>
    public static string EvidenceHash(string groupKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(groupKey)));
}

/// <summary>
/// Decides which section one finding belongs to. It is the only place that classifies findings, so a
/// file the run never read cannot drift into the verified section through a second code path.
/// </summary>
internal static class DedupFindingClassifier
{
    /// <summary>
    /// True when this run knows the file's length but never proved its bytes: it had no same-length
    /// peer, or it fell outside the read budget. This is a statement about what was compared.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "CA1506:Avoid excessive class coupling",
        Justification = "A classification rule is stated in its own vocabulary — plan-item state, read state and failure kind — so it lives in one place instead of being inferred at each render site.")]
    public static bool IsUnverified(DedupReportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.State == DedupPlanItemState.NotAnalyzed
            || item.ReadState is DedupItemReadState.NotRead or DedupItemReadState.SkippedByBudget
            || item.Sha256 is null;
    }

    /// <summary>
    /// True when this run failed to read a file that exists. A read failure is a statement about
    /// readability, so it is reported apart from content the run chose not to read.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "CA1506:Avoid excessive class coupling",
        Justification = "Same reason as IsUnverified: the rule is expressed in the report's own state vocabulary and must stay beside it.")]
    public static bool IsUnreadable(DedupReportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.State == DedupPlanItemState.Unreadable
            || item.ReadState == DedupItemReadState.ReadFailed
            || item.Failure is not (DedupReadFailure.None or DedupReadFailure.TooLarge);
    }
}

/// <summary>Maps one analyzed plan item onto its report shape without inventing a field.</summary>
internal static class DedupReportItemMapper
{
    public static DedupReportItem ToReportItem(DedupPlanItem item, IReadOnlyDictionary<string, string> roots)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(roots);
        var (root, relativePath) = DedupFileIdentity.Split(item.SourceIdentity);
        return new DedupReportItem(
            item.SourceId,
            roots.GetValueOrDefault(root) ?? root,
            relativePath,
            item.Length,
            item.Sha256,
            DedupStructureHashText.Format(item.StructureHash),
            item.LastWriteTimeUtc,
            item.State,
            item.ReadState,
            item.Failure,
            item.SkipReason,
            item.GroupKey,
            item.Category,
            item.Relations);
    }

    public static DedupPlanItem ToPlanItem(DedupReportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DedupPlanItem(
            DedupFileIdentity.Of(item),
            item.SourceId,
            item.Length,
            item.Sha256,
            DedupStructureHashText.Parse(item.StructureHash),
            item.LastWriteTimeUtc,
            item.State,
            item.ReadState,
            item.Failure,
            item.SkipReason,
            item.GroupKey,
            item.Category,
            item.Relations);
    }
}

/// <summary>
/// The textual form of a structure hash. It is a fixed-width hexadecimal string so a plan that is
/// stored and re-read compares equal instead of drifting through a culture-sensitive conversion.
/// </summary>
internal static class DedupStructureHashText
{
    public static string Format(long value) =>
        value.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);

    public static long Parse(string? value) =>
        value is not null
        && long.TryParse(
            value,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0L;
}
