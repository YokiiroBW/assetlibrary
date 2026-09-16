using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Domain;

/// <summary>
/// Decides which submitted sources this read-only analysis may actually read. The rules exist so a
/// preview can never turn a managed library or a publication output directory into a fresh inbound
/// source, which is the failure mode that would re-import the product's own output.
/// </summary>
public static class DedupScopePolicy
{
    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="ancestor"/> or lives below it.
    /// Both values are compared in their canonical '/' form so a source cannot pass the check on
    /// Windows and fail it on Linux for the same directory.
    /// </summary>
    public static bool IsWithin(CanonicalLibraryRoot candidate, CanonicalLibraryRoot ancestor)
    {
        var comparison = SelectComparison(candidate, ancestor);
        var candidateValue = Normalize(candidate.Value);
        var ancestorValue = Normalize(ancestor.Value);
        if (string.Equals(candidateValue, ancestorValue, comparison))
        {
            return true;
        }

        return candidateValue.StartsWith($"{ancestorValue}/", comparison);
    }

    /// <summary>
    /// True when two roots are the same physical directory or one contains the other.
    /// </summary>
    public static bool Overlaps(CanonicalLibraryRoot left, CanonicalLibraryRoot right) =>
        IsWithin(left, right) || IsWithin(right, left);

    public static DedupAnalysisScope Resolve(
        IReadOnlyList<DedupSourceRequest> sources,
        IReadOnlyList<CanonicalLibraryRoot> registeredRoots,
        IReadOnlyList<CanonicalLibraryRoot> managedOutputRoots)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(registeredRoots);
        ArgumentNullException.ThrowIfNull(managedOutputRoots);
        if (sources.Count == 0)
        {
            throw new ArgumentException("A dedup analysis requires at least one source.", nameof(sources));
        }

        if (sources.Select(source => source.SourceId).Distinct().Count() != sources.Count)
        {
            throw new ArgumentException("Every dedup source needs its own identity.", nameof(sources));
        }

        var accepted = new List<DedupSourceRequest>(sources.Count);
        var rejected = new List<DedupRejectedSourceRequest>();
        foreach (var source in sources)
        {
            var rejection = Inspect(source, accepted, rejected, registeredRoots, managedOutputRoots);
            if (rejection == DedupSourceRejection.None)
            {
                accepted.Add(source);
                continue;
            }

            rejected.Add(new DedupRejectedSourceRequest(source, rejection));
        }

        return new DedupAnalysisScope(accepted, rejected);
    }

    private static DedupSourceRejection Inspect(
        DedupSourceRequest source,
        List<DedupSourceRequest> accepted,
        List<DedupRejectedSourceRequest> rejected,
        IReadOnlyList<CanonicalLibraryRoot> registeredRoots,
        IReadOnlyList<CanonicalLibraryRoot> managedOutputRoots)
    {
        if (!IsPhysicallyUsable(source.Root))
        {
            return DedupSourceRejection.SourceRootInvalid;
        }

        // The same physical directory, or a directory that contains one already submitted, would be
        // walked twice and every file counted twice. The reverse relation (a parent submitted after
        // its child) is caught when that parent is inspected.
        if (accepted.Any(candidate => IsWithin(source.Root, candidate.Root))
            || rejected.Any(candidate => IsWithin(source.Root, candidate.Source.Root)))
        {
            return DedupSourceRejection.DuplicateSourceRegistration;
        }

        if (source.Role == DedupSourceRole.RegisteredLibrary
            && !registeredRoots.Any(root => IsWithin(source.Root, root)))
        {
            // A registered source must be one of this installation's own roots; an unknown library
            // id is not a licence to read an arbitrary directory.
            return DedupSourceRejection.RegisteredLibraryUnknown;
        }

        if (IsNestedInRegisteredLibrary(source, registeredRoots))
        {
            return DedupSourceRejection.ManagedLibraryOverlap;
        }

        if (managedOutputRoots.Any(root => Overlaps(source.Root, root)))
        {
            return DedupSourceRejection.OutputBackflow;
        }

        return DedupSourceRejection.None;
    }

    /// <summary>
    /// True when an inbound source sits inside a managed library root instead of being that root.
    /// Reading the library root itself is the normal in-place curation case; reading a directory
    /// nested in it is what would make a managed library look like fresh inbound content.
    /// </summary>
    private static bool IsNestedInRegisteredLibrary(
        DedupSourceRequest source,
        IReadOnlyList<CanonicalLibraryRoot> registeredRoots) =>
        source.Role == DedupSourceRole.InboundStaging
        && registeredRoots.Any(managed =>
            !IsWithin(managed, source.Root) && IsWithin(source.Root, managed));

    private static bool IsPhysicallyUsable(CanonicalLibraryRoot root)
    {
        var value = root.Value;
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
        {
            return false;
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            return false;
        }

        // A filesystem root ("/" or "C:/") would make every other root a child of this source.
        return segments.Length > 0 && !(segments.Length == 1 && segments[0].EndsWith(':'));
    }

    private static StringComparison SelectComparison(
        CanonicalLibraryRoot left,
        CanonicalLibraryRoot right) =>
        left.Comparison == RootPathComparison.CaseInsensitive
        || right.Comparison == RootPathComparison.CaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string Normalize(string value) =>
        value.Replace('\\', '/').TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";
}
