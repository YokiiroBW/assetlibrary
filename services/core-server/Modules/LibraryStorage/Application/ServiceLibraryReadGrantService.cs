using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.LibraryStorage.Application;

public interface IServiceLibraryReadGrantStore
{
    ValueTask SetAsync(ServiceLibraryReadGrantChange request, ServiceLibraryReadGrantOperation operation,
        DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class ServiceLibraryReadGrantService(IServiceLibraryReadGrantStore store, TimeProvider timeProvider)
    : IServiceLibraryReadGrantManagement
{
    public ValueTask SetAsync(ServiceLibraryReadGrantChange request, ServiceLibraryReadGrantOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.PrincipalId == Guid.Empty || request.LibraryIds is null
            || request.LibraryIds.Count is < 1 or > 100
            || request.LibraryIds.Any(id => id.Value == Guid.Empty)
            || request.LibraryIds.Distinct().Count() != request.LibraryIds.Count
            || operation.CorrelationId == Guid.Empty || string.IsNullOrWhiteSpace(operation.OperatorId)
            || operation.OperatorId.Length > 200 || operation.OperatorId != operation.OperatorId.Trim()
            || operation.OperatorId.Any(char.IsControl))
        {
            throw new ReadOnlyTrialException("invalid_request");
        }

        // Freeze the caller's mutable collection before the asynchronous persistence boundary.
        return store.SetAsync(request with { LibraryIds = request.LibraryIds.ToArray() }, operation,
            timeProvider.GetUtcNow(), cancellationToken);
    }
}
