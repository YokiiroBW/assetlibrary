using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.LibraryStorage.Application;

public sealed class LibraryCategoryService(ILibraryManagementStore store, TimeProvider timeProvider) : ILibraryCategoryManagement
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

        return store.UpdateCategoryAsync(request, operation, timeProvider.GetUtcNow(), cancellationToken);
    }
}
