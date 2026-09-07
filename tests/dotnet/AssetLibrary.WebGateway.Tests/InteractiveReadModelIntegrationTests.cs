using System.Globalization;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class InteractiveReadModelIntegrationTests
{
    [TestMethod]
    public async Task PostgreSqlPagesDetailsAnchorsAndScopesKeepTheirBoundaries()
    {
        var connection = Environment.GetEnvironmentVariable("ASSETLIBRARY_TEST_INTERACTIVE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Assert.Inconclusive("The required PostgreSQL driver supplies the interactive read fixture.");
        }

        await using var source = NpgsqlDataSource.Create(connection!);
        var queries = new PostgresAuthorizedReadModelQuery(source, new EphemeralDataProtectionProvider());
        var libraryId = new LibraryId(Guid.Parse(Environment.GetEnvironmentVariable("ASSETLIBRARY_TEST_INTERACTIVE_LIBRARY")!));
        var subject = new AuthenticatedSubject("oidc:interactive-reader");
        var request = new BrowseEntriesQuery(subject, libraryId, new BrowseParentPath("folder"),
            new ReadPageOptions(31, TimeSpan.FromSeconds(5)));
        var library = await queries.GetLibraryAsync(new(subject, libraryId, TimeSpan.FromSeconds(5)), CancellationToken.None);
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
            var nullRequest = request with { Page = new ReadPageOptions(1, TimeSpan.FromSeconds(5)),
                Options = new(EntrySortBy.Size, direction, EntryKindFilter.All), AnchorEntryId = nulls[0].EntryId };
            var nullPage = await queries.BrowseEntriesAsync(nullRequest, CancellationToken.None);
            Assert.IsNotNull(nullPage?.Page.NextCursor);
            var continuation = await queries.BrowseEntriesAsync(nullRequest with { AnchorEntryId = null,
                Page = new ReadPageOptions(1, TimeSpan.FromSeconds(5), nullPage.Page.NextCursor) }, CancellationToken.None);
            Assert.IsNotNull(continuation);
            Assert.AreEqual(nulls[1].EntryId, continuation.Page.Items[0].EntryId);
        }

        await AssertAnchorsAndDetailsAsync(queries, request, baseline);
        await AssertCursorScopesAsync(queries, request);
    }

    private static async Task AssertAnchorsAndDetailsAsync(PostgresAuthorizedReadModelQuery queries,
        BrowseEntriesQuery request, IReadOnlyList<ReadOnlyEntry> baseline)
    {
        var anchor = baseline[130];
        var anchored = await queries.BrowseEntriesAsync(request with { AnchorEntryId = anchor.EntryId }, CancellationToken.None);
        Assert.IsNotNull(anchored);
        CollectionAssert.AreEqual(baseline.Skip(130).Select(item => item.EntryId).ToArray(), anchored.Page.Items.Select(item => item.EntryId).ToArray());
        var earlier = await queries.BrowseEntriesAsync(request with { AnchorEntryId = baseline[100].EntryId }, CancellationToken.None);
        Assert.IsNotNull(earlier?.Page.NextCursor);
        var later = await queries.BrowseEntriesAsync(request with
        {
            Page = new ReadPageOptions(31, TimeSpan.FromSeconds(5), earlier.Page.NextCursor),
        }, CancellationToken.None);
        Assert.IsNotNull(later);
        Assert.AreEqual(baseline[131].EntryId, later.Page.Items[0].EntryId);
        var details = await queries.GetEntryAsync(new(request.Subject, request.LibraryId, anchor.EntryId, TimeSpan.FromSeconds(5)), CancellationToken.None);
        Assert.IsNotNull(details);
        Assert.AreEqual(anchor, details.Entry);
        var hiddenSubject = new AuthenticatedSubject("oidc:interactive-hidden");
        Assert.IsNull(await queries.GetEntryAsync(new(hiddenSubject, request.LibraryId, anchor.EntryId, TimeSpan.FromSeconds(5)), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AuthorizedReadNotFoundException>(async () =>
            await queries.BrowseEntriesAsync(request with { AnchorEntryId = anchor.EntryId,
                Options = new(EntrySortBy.Name, ReadSortDirection.Asc, EntryKindFilter.All, "never-matches") }, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AuthorizedReadNotFoundException>(async () =>
            await queries.SearchAssetsAsync(new(hiddenSubject, new AssetSearchText("entry"), request.Page,
                new(AssetSearchScope.Library, request.LibraryId)), CancellationToken.None));
    }

    private static async Task AssertCursorScopesAsync(PostgresAuthorizedReadModelQuery queries, BrowseEntriesQuery request)
    {
        var first = await queries.BrowseEntriesAsync(request, CancellationToken.None);
        Assert.IsNotNull(first?.Page.NextCursor);
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
            await queries.SearchAssetsAsync(searchRequest with { Scope = new(AssetSearchScope.Library, request.LibraryId),
                Page = new ReadPageOptions(31, TimeSpan.FromSeconds(5), search.NextCursor) }, CancellationToken.None));
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

    private static async Task<List<ReadOnlyEntry>> ReadAllAsync(PostgresAuthorizedReadModelQuery queries, BrowseEntriesQuery request)
    {
        var items = new List<ReadOnlyEntry>();
        for (var pageNumber = 0; pageNumber < 6; pageNumber++)
        {
            var page = await queries.BrowseEntriesAsync(request, CancellationToken.None);
            Assert.IsNotNull(page);
            items.AddRange(page.Page.Items);
            if (page.Page.NextCursor is null)
            {
                Assert.AreEqual(items.Count, items.Select(entry => entry.EntryId).Distinct().Count());
                return items;
            }

            request = request with { Page = new ReadPageOptions(31, TimeSpan.FromSeconds(5), page.Page.NextCursor) };
        }

        Assert.Fail("The bounded fixture pagination did not terminate.");
        return items;
    }

    private static IEnumerable<ReadOnlyEntry> Ordered(IEnumerable<ReadOnlyEntry> entries, EntrySortBy sort, ReadSortDirection direction)
    {
        var descending = direction == ReadSortDirection.Desc;
        var names = StringComparer.Ordinal;
        var ordered = entries.OrderBy(entry => sort == EntrySortBy.Size && entry.ContentLength is null ? 1 : 0);
        ordered = sort switch
        {
            EntrySortBy.Modified => descending ? ordered.ThenByDescending(entry => entry.LastWriteTimeUtc) : ordered.ThenBy(entry => entry.LastWriteTimeUtc),
            EntrySortBy.Size => descending ? ordered.ThenByDescending(entry => entry.ContentLength) : ordered.ThenBy(entry => entry.ContentLength),
            _ => ordered,
        };
        return descending
            ? ordered.ThenByDescending(entry => entry.RelativePath.Name.ToLowerInvariant(), names)
                .ThenByDescending(entry => entry.EntryId.Value.ToString("D", CultureInfo.InvariantCulture), names)
            : ordered.ThenBy(entry => entry.RelativePath.Name.ToLowerInvariant(), names)
                .ThenBy(entry => entry.EntryId.Value.ToString("D", CultureInfo.InvariantCulture), names);
    }
}
