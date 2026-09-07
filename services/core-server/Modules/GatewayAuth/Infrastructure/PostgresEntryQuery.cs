using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresEntryQuery(
    ProtectedReadCursorCodec cursors,
    PostgresBrowseReader reader)
{
    private readonly ProtectedReadCursorCodec cursors =
        cursors ?? throw new ArgumentNullException(nameof(cursors));
    private readonly PostgresBrowseReader reader =
        reader ?? throw new ArgumentNullException(nameof(reader));

    public async ValueTask<AuthorizedEntryPage?> BrowseAsync(
        BrowseEntriesQuery query,
        CancellationToken cancellationToken)
    {
        const string scope = "browse";
        var filter = $"{query.LibraryId.Value:D}\n{query.ParentPath.Value}\n{query.Options.SortBy}\n{query.Options.Direction}\n{query.Options.Kind}\n{query.Options.NameFilter}";
        var cursor = query.Page.Cursor is { } supplied
            ? cursors.Decode(supplied, scope, filter)
            : null;
        if (cursor is not null && (cursor.EntryId is null || cursor.LibraryId is not null
            || (query.Options.SortBy == EntrySortBy.Modified && cursor.Modified is null)))
        {
            throw new InvalidReadCursorException();
        }

        var result = await reader.ReadAsync(query, cursor, cancellationToken).ConfigureAwait(false);
        if (result.Library is null)
        {
            return null;
        }

        var rows = result.Rows;
        if (query.AnchorEntryId is not null && (rows.Count == 0 || rows[0].Entry.EntryId != query.AnchorEntryId))
        {
            throw new AuthorizedReadNotFoundException();
        }

        var next = PostgresReadPage.Complete(rows, query.Page.PageSize,
            row => cursors.Encode(scope, filter, row.SortName, null, row.Entry.EntryId.Value,
                row.Entry.LastWriteTimeUtc, row.Entry.ContentLength));
        return new AuthorizedEntryPage(
            result.Library,
            query.ParentPath,
            new ReadPage<ReadOnlyEntry>(rows.Select(row => row.Entry).ToArray(), next), query.AnchorEntryId);
    }
}
