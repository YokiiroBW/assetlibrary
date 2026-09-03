using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresBrowseReader(NpgsqlDataSource dataSource)
{
    private const string BrowseSql = """
        SELECT entry_id, relative_path, kind, content_length, last_write_time_utc, sort_name
        FROM gateway_auth.browse_authorized_entries($1, $2, $3, $4, $5, $6);
        """;
    private readonly NpgsqlDataSource dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async ValueTask<PostgresBrowseResult> ReadAsync(
        BrowseEntriesQuery query,
        DecodedReadCursor? cursor,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await PostgresReadExecutor.BeginReadOnlyAsync(
            connection,
            query.Page.Timeout,
            cancellationToken).ConfigureAwait(false);

        var library = await PostgresLibraryQuery.FindAsync(
            connection,
            transaction,
            query.Subject,
            query.LibraryId,
            query.Page.Timeout,
            cancellationToken).ConfigureAwait(false);
        if (library is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new PostgresBrowseResult(null, []);
        }

        var rows = await ReadRowsAsync(connection, transaction, query, cursor, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PostgresBrowseResult(library, rows);
    }

    private static async ValueTask<List<PostgresEntryRow>> ReadRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BrowseEntriesQuery query,
        DecodedReadCursor? cursor,
        CancellationToken cancellationToken)
    {
        await using var command = PostgresReadCommand.Create(
            connection,
            transaction,
            BrowseSql,
            query.Page.Timeout);
        PostgresReadCommand.Text(command, query.Subject.Value);
        PostgresReadCommand.Uuid(command, query.LibraryId.Value);
        PostgresReadCommand.Text(command, query.ParentPath.Value);
        PostgresReadCommand.NullableText(command, cursor?.SortName);
        PostgresReadCommand.NullableUuid(command, cursor?.EntryId);
        PostgresReadCommand.Integer(command, query.Page.PageSize + 1);

        var rows = new List<PostgresEntryRow>(query.Page.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new PostgresEntryRow(
                PostgresReadCommand.ReadEntry(reader, query.LibraryId, 0),
                reader.GetString(5)));
        }

        return rows;
    }
}

internal sealed record PostgresBrowseResult(
    AuthorizedLibrary? Library,
    List<PostgresEntryRow> Rows);

internal sealed record PostgresEntryRow(ReadOnlyEntry Entry, string SortName);
