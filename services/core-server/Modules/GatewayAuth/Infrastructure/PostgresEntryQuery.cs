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
        var filter = $"{query.LibraryId.Value:D}\n{query.ParentPath.Value}";
        var cursor = query.Page.Cursor is { } supplied
            ? cursors.Decode(supplied, scope, filter)
            : null;
        if (cursor is not null && (cursor.EntryId is null || cursor.LibraryId is not null))
        {
            throw new InvalidReadCursorException();
        }

        var result = await reader.ReadAsync(query, cursor, cancellationToken).ConfigureAwait(false);
        if (result.Library is null)
        {
            return null;
        }

        var rows = result.Rows;
        var hasMore = rows.Count > query.Page.PageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        ReadPageCursor? next = hasMore
            ? cursors.Encode(scope, filter, rows[^1].SortName, null, rows[^1].Entry.EntryId.Value)
            : null;
        return new AuthorizedEntryPage(
            result.Library,
            query.ParentPath,
            new ReadPage<ReadOnlyEntry>(rows.Select(row => row.Entry).ToArray(), next));
    }
}
