using System.Globalization;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.WebGateway.Tests;

internal static class InteractiveReadPageFixture
{
    public static async Task<List<ReadOnlyEntry>> ReadAllAsync(PostgresAuthorizedReadModelQuery queries, BrowseEntriesQuery request)
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

    public static IEnumerable<ReadOnlyEntry> Ordered(IEnumerable<ReadOnlyEntry> entries, EntrySortBy sort, ReadSortDirection direction)
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
