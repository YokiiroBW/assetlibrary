using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal static class AuthorizedReadResultValidator
{
    public static AuthorizedLibrary? Library(AuthorizedLibrary? result, LibraryId libraryId)
    {
        if (result is not null && (result.LibraryId != libraryId || !ValidLibrary(result)))
        {
            throw new InvalidOperationException("The authorized library query crossed its request boundary.");
        }

        return result;
    }

    public static AuthorizedEntryDetail? Detail(AuthorizedEntryDetail? result, GetEntryQuery request)
    {
        if (result is not null)
        {
            _ = Library(result.Library, request.LibraryId);
            if (result.Entry.LibraryId != request.LibraryId || result.Entry.EntryId != request.EntryId)
            {
                throw new InvalidOperationException("The authorized entry detail crossed its request boundary.");
            }
        }

        return result;
    }

    public static ReadPage<AuthorizedLibrary> Libraries(
        ReadPage<AuthorizedLibrary> result,
        int pageSize,
        LibraryCategory? category = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        Page(result.Items, result.NextCursor, pageSize);
        if (result.Items.Any(item =>
                !ValidLibrary(item) || (category is not null && item.Category != category)))
        {
            throw new InvalidOperationException("The authorized library query returned invalid data.");
        }

        return result;
    }

    public static AuthorizedEntryPage? Entries(
        AuthorizedEntryPage? result,
        BrowseEntriesQuery request,
        int pageSize)
    {
        if (result is null)
        {
            return null;
        }

        if (result.Library.LibraryId != request.LibraryId
            || result.ParentPath != request.ParentPath
            || !ValidLibrary(result.Library)
            || result.AnchorEntryId != request.AnchorEntryId)
        {
            throw new InvalidOperationException("The authorized entry query crossed its request boundary.");
        }

        Page(result.Page.Items, result.Page.NextCursor, pageSize);
        if (request.AnchorEntryId is { } anchor && (result.Page.Items.Count == 0 || result.Page.Items[0].EntryId != anchor))
        {
            throw new InvalidOperationException("The entry query did not begin at its requested anchor.");
        }

        if (result.Page.Items.Any(item =>
                item.LibraryId != request.LibraryId
                || !IsDirectChild(item.RelativePath.Value, request.ParentPath.Value)
                || !request.Options.Includes(item.Kind)))
        {
            throw new InvalidOperationException("The entry query returned an item outside the requested directory.");
        }

        return result;
    }

    public static ReadPage<AuthorizedSearchHit> Search(
        ReadPage<AuthorizedSearchHit> result,
        int pageSize,
        AssetSearchScopeOptions scope = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        Page(result.Items, result.NextCursor, pageSize);
        if (result.Items.Any(item =>
                item.Library.LibraryId != item.Entry.LibraryId
                || !ValidLibrary(item.Library)
                || !scope.Includes(item.Entry)
                || !Enum.IsDefined(item.Reason)))
        {
            throw new InvalidOperationException("The authorized search query returned invalid data.");
        }

        return result;
    }

    private static void Page<T>(
        IReadOnlyList<T>? items,
        ReadPageCursor? nextCursor,
        int pageSize)
    {
        if (items is null || items.Count > pageSize)
        {
            throw new InvalidOperationException("A read-only query returned an unbounded page.");
        }

        if (items.Count == 0 && nextCursor is not null)
        {
            throw new InvalidOperationException("An empty page cannot advertise a continuation cursor.");
        }
    }

    private static bool IsDirectChild(string relativePath, string parentPath)
    {
        var separator = relativePath.LastIndexOf('/');
        var actualParent = separator < 0 ? string.Empty : relativePath[..separator];
        return string.Equals(actualParent, parentPath, StringComparison.Ordinal);
    }

    private static bool ValidLibrary(AuthorizedLibrary item) =>
        item.LibraryId.Value != Guid.Empty && !string.IsNullOrWhiteSpace(item.DisplayName)
        && item.DisplayName.Length <= 200 && LibraryReadPolicy.CanRead(item.AccessLevel)
        && Enum.IsDefined(item.Category);
}
