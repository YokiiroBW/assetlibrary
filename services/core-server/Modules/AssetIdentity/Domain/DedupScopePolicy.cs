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

        // Every source is validated against the fixed registered and output roots first, so the
        // local overlap check below can compare roots only: a root that was refused is neutral
        // ground and must not be walked, but it must not decide the verdict for another source
        // either. Without this split the answer would depend on submission order.
        var inspected = new List<(DedupSourceRequest Source, DedupSourceRejection Rejection)>(sources.Count);
        foreach (var source in sources)
        {
            inspected.Add((source, InspectAgainstRoots(source, registeredRoots, managedOutputRoots)));
        }

        // A directory submitted both as a managed library and as exactly the same isolated inbound
        // staging root is a role contradiction, not a race: the registered side is refused as well,
        // so the verdict does not depend on which of the two happened to be listed first. A merely
        // nested staging directory is not a contradiction; it is refused on its own rule below.
        for (var index = 0; index < inspected.Count; index++)
        {
            var entry = inspected[index];
            if (entry.Rejection == DedupSourceRejection.None
                && entry.Source.Role == DedupSourceRole.RegisteredLibrary
                && inspected.Any(other =>
                    other.Source.Role == DedupSourceRole.InboundStaging
                    && IsWithin(entry.Source.Root, other.Source.Root)
                    && IsWithin(other.Source.Root, entry.Source.Root)))
            {
                inspected[index] = entry with { Rejection = DedupSourceRejection.ManagedLibraryOverlap };
            }
        }

        var accepted = new List<DedupSourceRequest>(sources.Count);
        var rejected = new List<DedupRejectedSourceRequest>();
        foreach (var entry in inspected)
        {
            var source = entry.Source;
            var rejection = entry.Rejection;

            // A source already refused on its own rule is neutral ground: it is not walked, so it
            // must not withdraw a valid sibling either. Only two otherwise-acceptable sources that
            // share physical ground are a duplicate registration.
            if (rejection == DedupSourceRejection.None
                && accepted.Any(candidate => Overlaps(source.Root, candidate.Root)))
            {
                rejection = DedupSourceRejection.DuplicateSourceRegistration;
            }

            if (rejection == DedupSourceRejection.None)
            {
                accepted.Add(source);
                continue;
            }

            rejected.Add(new DedupRejectedSourceRequest(source, rejection));
        }

        return new DedupAnalysisScope(accepted, rejected);
    }

    private static DedupSourceRejection InspectAgainstRoots(
        DedupSourceRequest source,
        IReadOnlyList<CanonicalLibraryRoot> registeredRoots,
        IReadOnlyList<CanonicalLibraryRoot> managedOutputRoots)
    {
        if (!IsPhysicallyUsable(source.Root))
        {
            return DedupSourceRejection.SourceRootInvalid;
        }

        if (source.Role == DedupSourceRole.RegisteredLibrary)
        {
            // A registered source must be one of this installation's own roots; an unknown library
            // id is not a licence to read an arbitrary directory.
            if (!registeredRoots.Any(root => IsWithin(source.Root, root)))
            {
                return DedupSourceRejection.RegisteredLibraryUnknown;
            }
        }
        else if (registeredRoots.Any(managed => Overlaps(source.Root, managed)))
        {
            // Inbound staging must be its own isolated directory. Being the library root itself, or
            // its parent, or anything inside it, would re-import managed content as fresh inbound.
            return DedupSourceRejection.ManagedLibraryOverlap;
        }

        if (managedOutputRoots.Any(root => Overlaps(source.Root, root)))
        {
            return DedupSourceRejection.OutputBackflow;
        }

        return DedupSourceRejection.None;
    }

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
