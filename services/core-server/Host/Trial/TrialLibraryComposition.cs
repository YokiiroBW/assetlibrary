using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal sealed record TrialLibraryServices(
    PostgresLibraryStore Store,
    ILibraryRegistration Registration,
    ILibraryCategoryManagement Categories,
    ILibraryAvailability Availability,
    IReadOnlyList<ConfiguredStorageSource> Sources);

internal static class TrialLibraryComposition
{
    public static TrialLibraryServices Create(
        TrialConfiguration configuration,
        TrialDatabaseConnections connections,
        ReadOnlyWorkerProcessOptions workers)
    {
        var sources = configuration.StorageSources.Select(source => new ConfiguredStorageSource(
            new StorageSourceId(source.StorageSourceId), source.SourceKey, source.DisplayName,
            new CanonicalLibraryRoot(source.AllowedRoot,
                source.CaseSensitive ? RootPathComparison.CaseSensitive : RootPathComparison.CaseInsensitive))).ToArray();
        var store = new PostgresLibraryStore(connections.Library);
        var probe = new ProcessLibraryRootProbe(workers);
        return new TrialLibraryServices(store,
            new LibraryRegistrationService(store, probe, sources, TimeProvider.System),
            new LibraryCategoryService(store, TimeProvider.System),
            new LibraryAvailabilityService(store, probe, sources, TimeProvider.System), sources);
    }
}
