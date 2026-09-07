using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.WebGateway.Tests;

internal static class InteractiveReadScopeAssertions
{
    public static async Task VerifyAsync(PostgresAuthorizedReadModelQuery queries, BrowseEntriesQuery request)
    {
        var first = await queries.BrowseEntriesAsync(request, CancellationToken.None);
        Assert.IsNotNull(first);
        Assert.IsNotNull(first.Page.NextCursor);
        var next = request with { Page = new ReadPageOptions(31, TimeSpan.FromSeconds(5), first.Page.NextCursor) };
        foreach (var options in new EntryBrowseOptions[]
        {
            new(EntrySortBy.Modified, ReadSortDirection.Asc, EntryKindFilter.All),
            new(EntrySortBy.Name, ReadSortDirection.Desc, EntryKindFilter.All),
            new(EntrySortBy.Name, ReadSortDirection.Asc, EntryKindFilter.Files),
            new(EntrySortBy.Name, ReadSortDirection.Asc, EntryKindFilter.All, "entry"),
        })
        {
            await Assert.ThrowsExactlyAsync<InvalidReadCursorException>(async () =>
                await queries.BrowseEntriesAsync(next with { Options = options }, CancellationToken.None));
        }

        var searchRequest = new SearchAssetsQuery(request.Subject, new AssetSearchText("entry"), request.Page,
            new(AssetSearchScope.Directory, request.LibraryId, request.ParentPath));
        var search = await queries.SearchAssetsAsync(searchRequest, CancellationToken.None);
        Assert.HasCount(31, search.Items);
        Assert.IsNotNull(search.NextCursor);
        Assert.IsTrue(search.Items.All(hit => hit.Entry.RelativePath.Value.StartsWith("folder/", StringComparison.Ordinal)));
        await Assert.ThrowsExactlyAsync<InvalidReadCursorException>(async () =>
            await queries.SearchAssetsAsync(searchRequest with
            {
                Scope = new(AssetSearchScope.Library, request.LibraryId),
                Page = new ReadPageOptions(31, TimeSpan.FromSeconds(5), search.NextCursor)
            }, CancellationToken.None));
        var boundary = await queries.SearchAssetsAsync(searchRequest with { SearchText = new AssetSearchText("boundary") }, CancellationToken.None);
        Assert.HasCount(1, boundary.Items);
        Assert.AreEqual("folder/nested/boundary.txt", boundary.Items[0].Entry.RelativePath.Value);
        var listed = await queries.ListLibrariesAsync(new(request.Subject, ReadPageOptions.Default, LibraryCategory.Images), CancellationToken.None);
        Assert.HasCount(1, listed.Items);
        Assert.AreEqual(request.LibraryId, listed.Items[0].LibraryId);
        var catalog = await queries.ListLibrariesAsync(new(request.Subject, new ReadPageOptions(1, TimeSpan.FromSeconds(5))), CancellationToken.None);
        Assert.IsNotNull(catalog.NextCursor);
        await Assert.ThrowsExactlyAsync<InvalidReadCursorException>(async () =>
            await queries.ListLibrariesAsync(new(request.Subject, new ReadPageOptions(1, TimeSpan.FromSeconds(5), catalog.NextCursor),
                LibraryCategory.Images), CancellationToken.None));
    }

}
