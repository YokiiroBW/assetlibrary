using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public interface IAuthorizedReadModelQuery
{
    ValueTask<AuthorizedLibrary?> GetLibraryAsync(GetLibraryQuery query, CancellationToken cancellationToken);

    ValueTask<AuthorizedEntryDetail?> GetEntryAsync(GetEntryQuery query, CancellationToken cancellationToken);

    ValueTask<ReadPage<AuthorizedLibrary>> ListLibrariesAsync(
        ListLibrariesQuery query,
        CancellationToken cancellationToken);

    ValueTask<AuthorizedEntryPage?> BrowseEntriesAsync(
        BrowseEntriesQuery query,
        CancellationToken cancellationToken);

    ValueTask<ReadPage<AuthorizedSearchHit>> SearchAssetsAsync(
        SearchAssetsQuery query,
        CancellationToken cancellationToken);
}
