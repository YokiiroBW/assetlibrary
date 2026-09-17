using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.CoreServer.Hosting.Trial;

/// <summary>
/// Answers which roots this installation may read, using only the library module's public queries.
/// Registered roots come from the library store; the physical allow-list is the configured storage
/// sources, so a database row cannot widen what the process is permitted to touch.
/// </summary>
internal sealed class TrialDedupSourceScope(
    ILibraryManagementStore libraries,
    IReadOnlyList<ConfiguredStorageSource> sources) : IDedupSourceScopeQuery
{
    public async ValueTask<IReadOnlyList<CanonicalLibraryRoot>> RegisteredRootsAsync(CancellationToken cancellationToken)
    {
        var registered = new List<CanonicalLibraryRoot>();
        foreach (var source in sources)
        {
            await foreach (var root in libraries
                .ListAsync(new StorageSourceId(source.StorageSourceId.Value), cancellationToken)
                .ConfigureAwait(false))
            {
                // A registered row outside the configured allow-list is not readable: it is reported
                // as no source at all rather than silently accepted because a row exists.
                if (IsWithinAllowList(root.Root, source.AllowedRoot))
                {
                    registered.Add(root.Root);
                }
            }
        }

        return registered;
    }

    /// <summary>
    /// This installation publishes assets inside the registered libraries themselves, so there is no
    /// separate managed output root to protect. The answer is an empty list, not a guess: inventing a
    /// root here would refuse a legitimate registered library.
    /// </summary>
    public ValueTask<IReadOnlyList<CanonicalLibraryRoot>> ManagedOutputRootsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<CanonicalLibraryRoot>>([]);
    }

    private static bool IsWithinAllowList(CanonicalLibraryRoot candidate, CanonicalLibraryRoot allowed)
    {
        var comparison = candidate.Comparison == RootPathComparison.CaseInsensitive
            || allowed.Comparison == RootPathComparison.CaseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        var candidateValue = Normalize(candidate.Value);
        var allowedValue = Normalize(allowed.Value);
        return string.Equals(candidateValue, allowedValue, comparison)
            || candidateValue.StartsWith($"{allowedValue}/", comparison);
    }

    private static string Normalize(string value) =>
        value.Replace('\\', '/').TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";
}
