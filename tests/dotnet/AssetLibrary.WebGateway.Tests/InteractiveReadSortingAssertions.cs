using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;

using static AssetLibrary.WebGateway.Tests.InteractiveReadPageFixture;

namespace AssetLibrary.WebGateway.Tests;

internal static class InteractiveReadSortingAssertions
{
    public static async Task<List<ReadOnlyEntry>> VerifyAsync(PostgresAuthorizedReadModelQuery queries, BrowseEntriesQuery request)
    {
        var library = await queries.GetLibraryAsync(new(request.Subject, request.LibraryId, TimeSpan.FromSeconds(5)), CancellationToken.None);
        Assert.IsNotNull(library);
        Assert.AreEqual(LibraryCategory.Images, library.Category);
        Assert.AreEqual(StorageAvailability.Offline, library.Availability);
        var baseline = await ReadAllAsync(queries, request);
        Assert.HasCount(155, baseline);
        foreach (var sort in Enum.GetValues<EntrySortBy>())
        {
            foreach (var direction in Enum.GetValues<ReadSortDirection>())
            {
                var actual = await ReadAllAsync(queries, request with { Options = new(sort, direction, EntryKindFilter.All) });
                var expected = Ordered(baseline, sort, direction).Select(entry => entry.EntryId).ToArray();
                CollectionAssert.AreEqual(expected, actual.Select(entry => entry.EntryId).ToArray());
            }
        }

        var literal = await queries.BrowseEntriesAsync(request with
        {
            Options = new(EntrySortBy.Name, ReadSortDirection.Asc, EntryKindFilter.Files, "  %_  "),
        }, CancellationToken.None);
        Assert.IsNotNull(literal);
        Assert.HasCount(1, literal.Page.Items);
        Assert.AreEqual("literal%_name.txt", literal.Page.Items[0].RelativePath.Name);
        var files = await ReadAllAsync(queries, request with { Options = new(EntrySortBy.Size, ReadSortDirection.Desc, EntryKindFilter.Files) });
        Assert.HasCount(153, files);
        Assert.AreEqual(9_007_199_254_740_999L, files[0].ContentLength);
        var directories = await ReadAllAsync(queries, request with { Options = new(EntrySortBy.Size, ReadSortDirection.Asc, EntryKindFilter.Directories) });
        Assert.HasCount(2, directories);
        foreach (var direction in Enum.GetValues<ReadSortDirection>())
        {
            var nulls = Ordered(baseline.Where(entry => entry.ContentLength is null), EntrySortBy.Size, direction).ToArray();
            var nullRequest = request with
            {
                Page = new ReadPageOptions(1, TimeSpan.FromSeconds(5)),
                Options = new(EntrySortBy.Size, direction, EntryKindFilter.All),
                AnchorEntryId = nulls[0].EntryId
            };
            var nullPage = await queries.BrowseEntriesAsync(nullRequest, CancellationToken.None);
            Assert.IsNotNull(nullPage);
            Assert.IsNotNull(nullPage.Page.NextCursor);
            var continuation = await queries.BrowseEntriesAsync(nullRequest with
            {
                AnchorEntryId = null,
                Page = new ReadPageOptions(1, TimeSpan.FromSeconds(5), nullPage.Page.NextCursor)
            }, CancellationToken.None);
            Assert.IsNotNull(continuation);
            Assert.AreEqual(nulls[1].EntryId, continuation.Page.Items[0].EntryId);
        }

        return baseline;
    }
}
