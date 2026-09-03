using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

public sealed class PostgresAuthorizedReadModelQuery : IAuthorizedReadModelQuery
{
    private readonly PostgresLibraryQuery libraries;
    private readonly PostgresEntryQuery entries;
    private readonly PostgresSearchQuery search;

    public PostgresAuthorizedReadModelQuery(
        NpgsqlDataSource dataSource,
        IDataProtectionProvider protectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var cursors = new ProtectedReadCursorCodec(protectionProvider);
        libraries = new PostgresLibraryQuery(dataSource, cursors);
        entries = new PostgresEntryQuery(cursors, new PostgresBrowseReader(dataSource));
        search = new PostgresSearchQuery(dataSource, cursors);
    }

    public ValueTask<ReadPage<AuthorizedLibrary>> ListLibrariesAsync(
        ListLibrariesQuery query,
        CancellationToken cancellationToken) =>
        libraries.ListAsync(query, cancellationToken);

    public ValueTask<AuthorizedEntryPage?> BrowseEntriesAsync(
        BrowseEntriesQuery query,
        CancellationToken cancellationToken) =>
        entries.BrowseAsync(query, cancellationToken);

    public ValueTask<ReadPage<AuthorizedSearchHit>> SearchAssetsAsync(
        SearchAssetsQuery query,
        CancellationToken cancellationToken) =>
        search.ExecuteAsync(query, cancellationToken);
}
