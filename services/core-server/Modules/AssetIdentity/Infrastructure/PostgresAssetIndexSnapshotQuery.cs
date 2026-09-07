using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.AssetIdentity.Infrastructure;

public sealed class PostgresAssetIndexSnapshotQuery(NpgsqlDataSource dataSource) : IAssetIndexSnapshotQuery, IInitialScanStageMaintenance
{
    public async ValueTask<InitialIndexSnapshot?> FindAsync(LibraryId libraryId, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await using var connection = await dataSource.OpenConnectionAsync(deadline.Token).ConfigureAwait(false);
        await PostgresAssetSessionCommands.InitializeAsync(connection, deadline.Token).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT scan_id,entry_count,observed_at FROM asset_identity.find_initial_snapshot($1)", connection)
        { CommandTimeout = 5 };
        command.Parameters.Add(new NpgsqlParameter { Value = libraryId.Value });
        await using var reader = await command.ExecuteReaderAsync(deadline.Token).ConfigureAwait(false);
        return await reader.ReadAsync(deadline.Token).ConfigureAwait(false)
            ? new InitialIndexSnapshot(reader.GetGuid(0), reader.GetInt32(1), reader.GetFieldValue<DateTimeOffset>(2)) : null;
    }

    public async ValueTask AbortAsync(Guid scanId, LibraryId libraryId, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = await dataSource.OpenConnectionAsync(deadline.Token).ConfigureAwait(false);
        await PostgresAssetSessionCommands.InitializeAsync(connection, deadline.Token).ConfigureAwait(false);
        var locked = await PostgresAssetSessionCommands.ScalarAsync(connection,
            "SELECT asset_identity.lock_initial_scan($1)", [libraryId.Value], deadline.Token).ConfigureAwait(false);
        if (locked is not true)
        {
            throw new ReadOnlyTrialException("scan_already_running");
        }

        try
        {
            _ = await PostgresAssetSessionCommands.ScalarAsync(connection,
                "SELECT asset_identity.abort_initial_scan($1)", [scanId], deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _ = await PostgresAssetSessionCommands.ScalarAsync(connection,
                "SELECT asset_identity.unlock_initial_scan($1)", [libraryId.Value], cleanup.Token).ConfigureAwait(false);
        }
    }
}

internal static class PostgresAssetSessionCommands
{
    public static async ValueTask InitializeAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SET ROLE assetlibrary_asset_identity_runtime; SET statement_timeout='120s'; SET lock_timeout='4s';", connection)
        { CommandTimeout = 5 };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<object?> ScalarAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
        foreach (var value in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }
}
