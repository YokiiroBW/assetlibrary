using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresSearchQuery(
    NpgsqlDataSource dataSource,
    ProtectedReadCursorCodec cursors)
{
    private const string SearchSql = """
        SELECT library_id, library_display_name, availability, access_level,
               entry_id, relative_path, kind, content_length, last_write_time_utc,
               hit_reason, sort_name
        FROM gateway_auth.search_authorized_entries($1, $2, $3, $4, $5, $6);
        """;
    private readonly NpgsqlDataSource dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly ProtectedReadCursorCodec cursors =
        cursors ?? throw new ArgumentNullException(nameof(cursors));

    public async ValueTask<ReadPage<AuthorizedSearchHit>> ExecuteAsync(
        SearchAssetsQuery query,
        CancellationToken cancellationToken)
    {
        const string scope = "search";
        var filter = query.SearchText.Value;
        var cursor = query.Page.Cursor is { } supplied
            ? cursors.Decode(supplied, scope, filter)
            : null;
        if (cursor is not null && (cursor.LibraryId is null || cursor.EntryId is null))
        {
            throw new InvalidReadCursorException();
        }

        var rows = await PostgresReadExecutor.ReadAsync(
            dataSource,
            SearchSql,
            query.Page.Timeout,
            query.Page.PageSize + 1,
            command =>
            {
                PostgresReadCommand.Text(command, query.Subject.Value);
                PostgresReadCommand.Text(command, query.SearchText.Value);
                PostgresReadCommand.NullableText(command, cursor?.SortName);
                PostgresReadCommand.NullableUuid(command, cursor?.LibraryId);
                PostgresReadCommand.NullableUuid(command, cursor?.EntryId);
                PostgresReadCommand.Integer(command, query.Page.PageSize + 1);
            },
            reader => new SearchRow(PostgresReadCommand.ReadSearchHit(reader), reader.GetString(10)),
            cancellationToken).ConfigureAwait(false);

        var hasMore = rows.Count > query.Page.PageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        ReadPageCursor? next = hasMore
            ? cursors.Encode(
                scope,
                filter,
                rows[^1].SortName,
                rows[^1].Hit.Library.LibraryId.Value,
                rows[^1].Hit.Entry.EntryId.Value)
            : null;
        return new ReadPage<AuthorizedSearchHit>(rows.Select(row => row.Hit).ToArray(), next);
    }

    private sealed record SearchRow(AuthorizedSearchHit Hit, string SortName);
}
