using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.WebGateway.Tests;

internal sealed class FakeAuthorizedReadModelQuery : IAuthorizedReadModelQuery
{
    public Func<ListLibrariesQuery, CancellationToken, ValueTask<ReadPage<AuthorizedLibrary>>> List { get; set; } =
        static (_, _) => ValueTask.FromResult(new ReadPage<AuthorizedLibrary>([], null));

    public Func<BrowseEntriesQuery, CancellationToken, ValueTask<AuthorizedEntryPage?>> Browse { get; set; } =
        static (_, _) => ValueTask.FromResult<AuthorizedEntryPage?>(null);

    public Func<SearchAssetsQuery, CancellationToken, ValueTask<ReadPage<AuthorizedSearchHit>>> Search { get; set; } =
        static (_, _) => ValueTask.FromResult(new ReadPage<AuthorizedSearchHit>([], null));

    public int CallCount { get; private set; }

    public ValueTask<ReadPage<AuthorizedLibrary>> ListLibrariesAsync(
        ListLibrariesQuery query,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return List(query, cancellationToken);
    }

    public ValueTask<AuthorizedEntryPage?> BrowseEntriesAsync(
        BrowseEntriesQuery query,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return Browse(query, cancellationToken);
    }

    public ValueTask<ReadPage<AuthorizedSearchHit>> SearchAssetsAsync(
        SearchAssetsQuery query,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return Search(query, cancellationToken);
    }
}
