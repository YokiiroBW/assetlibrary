using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Domain;

namespace AssetLibrary.Modules.LibraryStorage.Application;

public enum LibraryRootProbeStatus
{
    Available = 0,
    Missing = 1,
    Inaccessible = 2,
}

public sealed record LibraryRootProbeResult(
    LibraryRootProbeStatus Status,
    CanonicalLibraryRoot Root);

public interface ILibraryRootProbe
{
    ValueTask<LibraryRootProbeResult> ProbeAsync(
        string path,
        RootPathComparison comparison,
        CancellationToken cancellationToken);
}

public enum LibraryRootValidationStatus
{
    Valid = 0,
    Missing = 1,
    Inaccessible = 2,
    Overlapping = 3,
}

public sealed record LibraryRootValidationResult(
    LibraryRootValidationStatus Status,
    CanonicalLibraryRoot Root,
    LibraryRootOverlap? Overlap);

public sealed class LibraryRootValidationService(
    ILibraryRootProbe probe,
    IRegisteredLibraryRootQuery registeredRoots)
{
    public async ValueTask<LibraryRootValidationResult> ValidateAsync(
        LibraryId candidateLibraryId,
        StorageSourceId storageSourceId,
        string path,
        RootPathComparison comparison,
        CancellationToken cancellationToken)
    {
        var inspection = await probe.ProbeAsync(path, comparison, cancellationToken).ConfigureAwait(false);
        var unavailableStatus = inspection.Status switch
        {
            LibraryRootProbeStatus.Missing => LibraryRootValidationStatus.Missing,
            LibraryRootProbeStatus.Inaccessible => LibraryRootValidationStatus.Inaccessible,
            _ => (LibraryRootValidationStatus?)null,
        };
        if (unavailableStatus is not null)
        {
            return new LibraryRootValidationResult(unavailableStatus.Value, inspection.Root, null);
        }

        var candidate = new RegisteredLibraryRoot(candidateLibraryId, storageSourceId, inspection.Root);
        await foreach (var existing in registeredRoots
            .ListAsync(storageSourceId, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            if (existing.LibraryId == candidateLibraryId)
            {
                continue;
            }

            var overlap = LibraryRootOverlapPolicy.Find(candidate, existing);
            if (overlap is not null)
            {
                return new LibraryRootValidationResult(
                    LibraryRootValidationStatus.Overlapping,
                    inspection.Root,
                    overlap);
            }
        }

        return new LibraryRootValidationResult(LibraryRootValidationStatus.Valid, inspection.Root, null);
    }
}
