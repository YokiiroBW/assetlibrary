using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.LibraryStorage.Application;

public interface ILibraryManagementStore : ILibraryScanTargetQuery, IRegisteredLibraryRootQuery
{
    ValueTask<LibraryId?> FindRegistrationAsync(LibraryRegistrationRequest request, ManagementOperation operation, CancellationToken token);
    ValueTask<LibraryId> RegisterAsync(ConfiguredStorageSource source, LibraryRegistrationRequest request,
        CanonicalLibraryRoot root, ManagementOperation operation, DateTimeOffset now, CancellationToken token);
    ValueTask SetAvailabilityAsync(LibraryId libraryId, StorageAvailability availability, string? reason, DateTimeOffset now, CancellationToken token);
    ValueTask<IReadOnlyList<LibraryId>> NextProbesAsync(DateTimeOffset before, int limit, CancellationToken cancellationToken);
}

public static class LibraryRootContainment
{
    public static bool Contains(CanonicalLibraryRoot allowed, CanonicalLibraryRoot candidate)
    {
        var comparison = allowed.Comparison == RootPathComparison.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(allowed.Value, candidate.Value, comparison)
            || candidate.Value.StartsWith(allowed.Value.EndsWith('/') ? allowed.Value : allowed.Value + '/', comparison);
    }
}

public sealed class LibraryRegistrationService(
    ILibraryManagementStore store,
    ILibraryRootProbe probe,
    IReadOnlyList<ConfiguredStorageSource> sources,
    TimeProvider timeProvider) : ILibraryRegistration
{
    public async ValueTask<LibraryId> RegisterAsync(
        LibraryRegistrationRequest request, ManagementOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        operation.Validate();
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 200
            || request.DisplayName != request.DisplayName.Trim() || request.DisplayName.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(request.RootPath) || request.RootPath.Length > 4096)
        {
            throw new ReadOnlyTrialException("invalid_request");
        }

        var source = sources.SingleOrDefault(item => item.SourceKey == request.SourceKey)
            ?? throw new ReadOnlyTrialException("storage_source_not_allowed");
        var candidate = new CanonicalLibraryRoot(request.RootPath, source.AllowedRoot.Comparison);
        if (!LibraryRootContainment.Contains(source.AllowedRoot, candidate))
        {
            throw new ReadOnlyTrialException("root_not_allowed");
        }

        if (await store.FindRegistrationAsync(request, operation, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return existing;
        }

        var validation = await new LibraryRootValidationService(probe, store).ValidateAsync(
            LibraryId.New(), source.StorageSourceId, request.RootPath, source.AllowedRoot.Comparison, cancellationToken).ConfigureAwait(false);
        if (validation.Status != LibraryRootValidationStatus.Valid)
        {
            throw new ReadOnlyTrialException(validation.Status switch
            {
                LibraryRootValidationStatus.Overlapping => "root_overlap",
                LibraryRootValidationStatus.Inaccessible => "root_inaccessible",
                _ => "root_unavailable",
            });
        }

        if (!LibraryRootContainment.Contains(source.AllowedRoot, validation.Root))
        {
            throw new ReadOnlyTrialException("root_not_allowed");
        }

        return await store.RegisterAsync(source, request, validation.Root, operation,
            timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }
}

public sealed class LibraryAvailabilityService(
    ILibraryManagementStore store,
    ILibraryRootProbe probe,
    IReadOnlyList<ConfiguredStorageSource> sources,
    TimeProvider timeProvider) : ILibraryAvailability
{
    public async ValueTask<StorageAvailability> RefreshAsync(LibraryId libraryId, CancellationToken cancellationToken)
    {
        var target = await store.FindAsync(libraryId, cancellationToken).ConfigureAwait(false)
            ?? throw new ReadOnlyTrialException("library_not_found");
        var source = sources.SingleOrDefault(item => item.StorageSourceId == target.StorageSourceId);
        var availability = StorageAvailability.Offline;
        string? reason = "storage_configuration_unavailable";
        if (source is not null && LibraryRootContainment.Contains(source.AllowedRoot, target.Root))
        {
            try
            {
                var result = await probe.ProbeAsync(target.Root.Value, target.Root.Comparison, cancellationToken).ConfigureAwait(false);
                availability = result.Status == LibraryRootProbeStatus.Available ? StorageAvailability.Online : StorageAvailability.Offline;
                reason = availability == StorageAvailability.Online ? null : "storage_unavailable";
            }
            catch (ReadOnlyTrialException exception)
            {
                reason = exception.Code;
            }
        }

        await store.SetAvailabilityAsync(libraryId, availability, reason, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return availability;
    }

    public async ValueTask<int> RefreshBatchAsync(CancellationToken cancellationToken)
    {
        var libraries = await store.NextProbesAsync(timeProvider.GetUtcNow() - TimeSpan.FromSeconds(30), 4, cancellationToken)
            .ConfigureAwait(false);
        foreach (var libraryId in libraries)
        {
            await RefreshAsync(libraryId, cancellationToken).ConfigureAwait(false);
        }

        return libraries.Count;
    }
}
