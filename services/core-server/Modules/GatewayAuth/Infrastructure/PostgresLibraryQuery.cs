using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresLibraryQuery(
    NpgsqlDataSource dataSource,
    ProtectedReadCursorCodec cursors)
{
    private const string ListSql = """
        SELECT library_id, display_name, availability, access_level, sort_name
        FROM gateway_auth.list_authorized_libraries($1, $2, $3, $4);
        """;
    private const string FindSql = """
        SELECT library_id, display_name, availability, access_level
        FROM gateway_auth.find_authorized_library($1, $2);
        """;
    private readonly NpgsqlDataSource dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly ProtectedReadCursorCodec cursors =
        cursors ?? throw new ArgumentNullException(nameof(cursors));

    public async ValueTask<ReadPage<AuthorizedLibrary>> ListAsync(
        ListLibrariesQuery query,
        CancellationToken cancellationToken)
    {
        const string scope = "libraries";
        var cursor = query.Page.Cursor is { } supplied
            ? cursors.Decode(supplied, scope, string.Empty)
            : null;
        if (cursor is not null && (cursor.LibraryId is null || cursor.EntryId is not null))
        {
            throw new InvalidReadCursorException();
        }

        var rows = await PostgresReadExecutor.ReadAsync(
            dataSource,
            ListSql,
            query.Page.Timeout,
            query.Page.PageSize + 1,
            command =>
            {
                PostgresReadCommand.Text(command, query.Subject.Value);
                PostgresReadCommand.NullableText(command, cursor?.SortName);
                PostgresReadCommand.NullableUuid(command, cursor?.LibraryId);
                PostgresReadCommand.Integer(command, query.Page.PageSize + 1);
            },
            reader => new LibraryRow(PostgresReadCommand.ReadLibrary(reader, 0), reader.GetString(4)),
            cancellationToken).ConfigureAwait(false);

        var hasMore = rows.Count > query.Page.PageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        ReadPageCursor? next = hasMore
            ? cursors.Encode(scope, string.Empty, rows[^1].SortName, rows[^1].Library.LibraryId.Value, null)
            : null;
        return new ReadPage<AuthorizedLibrary>(rows.Select(row => row.Library).ToArray(), next);
    }

    public static async ValueTask<AuthorizedLibrary?> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuthenticatedSubject subject,
        LibraryId libraryId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await using var command = PostgresReadCommand.Create(connection, transaction, FindSql, timeout);
        PostgresReadCommand.Text(command, subject.Value);
        PostgresReadCommand.Uuid(command, libraryId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? PostgresReadCommand.ReadLibrary(reader, 0)
            : null;
    }

    private sealed record LibraryRow(AuthorizedLibrary Library, string SortName);
}
