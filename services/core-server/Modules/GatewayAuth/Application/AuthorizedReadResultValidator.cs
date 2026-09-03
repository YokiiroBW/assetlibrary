using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal static class AuthorizedReadResultValidator
{
    public static ReadPage<AuthorizedLibrary> Libraries(
        ReadPage<AuthorizedLibrary> result,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(result);
        Page(result.Items, result.NextCursor, pageSize);
        if (result.Items.Any(item =>
                item.LibraryId.Value == Guid.Empty
                || string.IsNullOrWhiteSpace(item.DisplayName)
                || item.DisplayName.Length > 200
                || !LibraryReadPolicy.CanRead(item.AccessLevel)))
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
            || !LibraryReadPolicy.CanRead(result.Library.AccessLevel))
        {
            throw new InvalidOperationException("The authorized entry query crossed its request boundary.");
        }

        Page(result.Page.Items, result.Page.NextCursor, pageSize);
        if (result.Page.Items.Any(item =>
                item.LibraryId != request.LibraryId
                || !IsDirectChild(item.RelativePath.Value, request.ParentPath.Value)))
        {
            throw new InvalidOperationException("The entry query returned an item outside the requested directory.");
        }

        return result;
    }

    public static ReadPage<AuthorizedSearchHit> Search(
        ReadPage<AuthorizedSearchHit> result,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(result);
        Page(result.Items, result.NextCursor, pageSize);
        if (result.Items.Any(item =>
                item.Library.LibraryId != item.Entry.LibraryId
                || !LibraryReadPolicy.CanRead(item.Library.AccessLevel)
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
}
