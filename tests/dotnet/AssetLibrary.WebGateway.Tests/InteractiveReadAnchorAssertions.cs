using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.WebGateway.Tests;

internal static class InteractiveReadAnchorAssertions
{
    public static async Task VerifyAsync(PostgresAuthorizedReadModelQuery queries,
        BrowseEntriesQuery request, IReadOnlyList<ReadOnlyEntry> baseline)
    {
        var anchor = baseline[130];
        var anchored = await queries.BrowseEntriesAsync(request with { AnchorEntryId = anchor.EntryId }, CancellationToken.None);
        Assert.IsNotNull(anchored);
        CollectionAssert.AreEqual(baseline.Skip(130).Select(item => item.EntryId).ToArray(), anchored.Page.Items.Select(item => item.EntryId).ToArray());
        var earlier = await queries.BrowseEntriesAsync(request with { AnchorEntryId = baseline[100].EntryId }, CancellationToken.None);
        Assert.IsNotNull(earlier);
        Assert.IsNotNull(earlier.Page.NextCursor);
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
            await queries.BrowseEntriesAsync(request with
            {
                AnchorEntryId = anchor.EntryId,
                Options = new(EntrySortBy.Name, ReadSortDirection.Asc, EntryKindFilter.All, "never-matches")
            }, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AuthorizedReadNotFoundException>(async () =>
            await queries.SearchAssetsAsync(new(hiddenSubject, new AssetSearchText("entry"), request.Page,
                new(AssetSearchScope.Library, request.LibraryId)), CancellationToken.None));
    }

}
