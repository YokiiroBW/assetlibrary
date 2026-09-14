namespace AssetLibrary.Modules.LibraryStorage.Contracts;

public sealed record ServiceLibraryReadGrantChange(Guid PrincipalId, IReadOnlyList<LibraryId> LibraryIds, bool Granted);

// Only the trusted local operator adapter may supply the verified operator identity.
public sealed record ServiceLibraryReadGrantOperation(string OperatorId, Guid CorrelationId);

public interface IServiceLibraryReadGrantManagement
{
    ValueTask SetAsync(ServiceLibraryReadGrantChange request, ServiceLibraryReadGrantOperation operation,
        CancellationToken cancellationToken);
}
