using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.AssetIdentity.Infrastructure;

public sealed class PostgresAssetObservationSink(NpgsqlDataSource dataSource) : IAssetObservationSink
{
    public async ValueTask<IAssetObservationSession> BeginInitialScanAsync(Guid scanId, LibraryId libraryId,
        DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var connection = await dataSource.OpenConnectionAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            await PostgresAssetSessionCommands.LockAsync(connection, libraryId, deadline.Token).ConfigureAwait(false);

            return new PostgresAssetObservationSession(connection, scanId, libraryId, startedAt);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

internal sealed class PostgresAssetObservationSession(
    NpgsqlConnection connection, Guid scanId, LibraryId libraryId, DateTimeOffset startedAt) : IAssetObservationSession
{
    public async ValueTask StageAsync(IReadOnlyList<AssetObservation> observations, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(observations.Count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(observations.Count, 1024);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await using var transaction = await connection.BeginTransactionAsync(deadline.Token).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(connection, transaction) { Timeout = 30 };
        foreach (var observation in observations)
        {
            observation.Validate();
            var command = new NpgsqlBatchCommand(
                "INSERT INTO asset_identity.scan_observation_stage(scan_id,entry_id,library_id,normalized_relative_path,kind," +
                "content_length,last_write_time_utc,staged_at) VALUES($1,$2,$3,$4,$5::asset_identity.entry_kind,$6,$7,$8)");
            foreach (var value in new object[] { scanId, observation.EntryId.Value, libraryId.Value, observation.RelativePath.Value, Kind(observation.Kind) })
            {
                command.Parameters.Add(new NpgsqlParameter { Value = value });
            }

            command.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Bigint,
                Value = observation.ContentLength is null ? DBNull.Value : observation.ContentLength.Value
            });
            command.Parameters.Add(new NpgsqlParameter { Value = observation.LastWriteTimeUtc });
            command.Parameters.Add(new NpgsqlParameter { Value = startedAt });
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(deadline.Token).ConfigureAwait(false);
        await transaction.CommitAsync(deadline.Token).ConfigureAwait(false);
    }

    public async ValueTask<int> CompleteAsync(DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        return (int)(await PostgresAssetSessionCommands.ScalarAsync(connection,
            "SELECT asset_identity.commit_initial_scan($1,$2,$3)", [scanId, libraryId.Value, observedAt], deadline.Token).ConfigureAwait(false))!;
    }

    public async ValueTask AbortAsync(CancellationToken cancellationToken)
    {
        _ = await PostgresAssetSessionCommands.ScalarAsync(connection,
            "SELECT asset_identity.abort_initial_scan($1)", [scanId], cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                _ = await PostgresAssetSessionCommands.ScalarAsync(connection,
                    "SELECT asset_identity.unlock_initial_scan($1)", [libraryId.Value], deadline.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string Kind(AssetEntryKind kind) => kind switch
    {
        AssetEntryKind.File => "file",
        AssetEntryKind.Directory => "directory",
        AssetEntryKind.ReparseFile => "reparse_file",
        AssetEntryKind.ReparseDirectory => "reparse_directory",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
