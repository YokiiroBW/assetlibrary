using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresEntryDetailQuery(NpgsqlDataSource dataSource)
{
    public async ValueTask<AuthorizedEntryDetail?> GetAsync(GetEntryQuery query, CancellationToken cancellationToken)
    {
        var rows = await PostgresReadExecutor.ReadAsync(dataSource,
            "SELECT library_id,library_display_name,availability,access_level,category," +
            "entry_id,relative_path,kind,content_length,last_write_time_utc FROM gateway_auth.find_authorized_entry($1,$2,$3)",
            query.Timeout, 1, command =>
            {
                PostgresReadCommand.Text(command, query.Subject.Value);
                PostgresReadCommand.Uuid(command, query.LibraryId.Value);
                PostgresReadCommand.Uuid(command, query.EntryId.Value);
            }, reader => new AuthorizedEntryDetail(PostgresReadCommand.ReadLibrary(reader, 0),
                PostgresReadCommand.ReadEntry(reader, query.LibraryId, 5)), cancellationToken).ConfigureAwait(false);
        return rows.SingleOrDefault();
    }
}
