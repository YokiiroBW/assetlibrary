using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public interface IAuthorizedReadModelQuery
{
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
