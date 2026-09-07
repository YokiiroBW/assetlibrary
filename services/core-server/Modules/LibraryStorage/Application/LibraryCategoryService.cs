using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.LibraryStorage.Application;

public interface ILibraryCategoryStore
{
    ValueTask<LibraryCategory> UpdateAsync(LibraryCategoryUpdate request, ManagementOperation operation,
        DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class LibraryCategoryService(ILibraryCategoryStore store, TimeProvider timeProvider) : ILibraryCategoryManagement
{
    public ValueTask<LibraryCategory> UpdateAsync(
        LibraryCategoryUpdate request, ManagementOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        operation.Validate();
        if (request.LibraryId.Value == Guid.Empty || !Enum.IsDefined(request.Category) || !Enum.IsDefined(request.ExpectedCategory))
        {
            throw new ReadOnlyTrialException("invalid_request");
        }

        return store.UpdateAsync(request, operation, timeProvider.GetUtcNow(), cancellationToken);
    }
}
