using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.LibraryStorage.Domain;

public enum LibraryRootOverlapKind
{
    SameRoot = 0,
    CandidateContainsExisting = 1,
    ExistingContainsCandidate = 2,
}

public sealed record LibraryRootOverlap(
    LibraryId ExistingLibraryId,
    LibraryRootOverlapKind Kind);

public static class LibraryRootOverlapPolicy
{
    public static LibraryRootOverlap? Find(
        RegisteredLibraryRoot candidate,
        RegisteredLibraryRoot existing)
    {
        if (candidate.StorageSourceId != existing.StorageSourceId)
        {
            return null;
        }

        var comparison = SelectComparison(candidate.Root.Comparison, existing.Root.Comparison);
        if (string.Equals(candidate.Root.Value, existing.Root.Value, comparison))
        {
            return new LibraryRootOverlap(existing.LibraryId, LibraryRootOverlapKind.SameRoot);
        }

        if (Contains(candidate.Root.Value, existing.Root.Value, comparison))
        {
            return new LibraryRootOverlap(
                existing.LibraryId,
                LibraryRootOverlapKind.CandidateContainsExisting);
        }

        return Contains(existing.Root.Value, candidate.Root.Value, comparison)
            ? new LibraryRootOverlap(existing.LibraryId, LibraryRootOverlapKind.ExistingContainsCandidate)
            : null;
    }

    private static StringComparison SelectComparison(
        RootPathComparison candidate,
        RootPathComparison existing) =>
        candidate == RootPathComparison.CaseInsensitive || existing == RootPathComparison.CaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool Contains(string parent, string child, StringComparison comparison)
    {
        var prefix = parent.EndsWith('/') ? parent : $"{parent}/";
        return child.StartsWith(prefix, comparison);
    }
}
