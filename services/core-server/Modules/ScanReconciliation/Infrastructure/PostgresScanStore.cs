using AssetLibrary.Infrastructure.Postgres;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.ScanReconciliation.Infrastructure;

public sealed class PostgresScanStore(NpgsqlDataSource dataSource) : IScanRequestStore
{
    private const string Columns = "request.task_id,request.library_id,request.created_at,request.dispatched,request.terminal,request.cancellation_requested_at IS NOT NULL";
    private readonly ModulePostgresSession database = new(dataSource, ModuleDatabaseRole.ScanReconciliation);

    public async ValueTask<ScanRequestRecord?> FindOperationAsync(LibraryId libraryId, ManagementOperation operation, CancellationToken token)
    {
        var request = await ReadRequestAsync(
            "JOIN scan_reconciliation.scan_operation operation USING(task_id) WHERE operation.principal_id=$1 " +
            "AND operation.operation='start' AND operation.idempotency_key=$2", [operation.PrincipalId, operation.IdempotencyKey], token).ConfigureAwait(false);
        if (request is not null && request.LibraryId != libraryId)
        {
            throw new ReadOnlyTrialException("idempotency_conflict");
        }

        return request;
    }

    public async ValueTask<ScanRequestRecord> AcceptAsync(LibraryId libraryId, ManagementOperation operation, CancellationToken cancellationToken)
    {
        Guid id;
        try
        {
            id = await database.RunAsync(async (connection, transaction, token) =>
            {
                await using var command = ModulePostgresSession.Command(connection, transaction,
                    "SELECT scan_reconciliation.accept_initial_scan($1,$2,$3,$4,$5)", operation.PrincipalId,
                    operation.IdempotencyKey, libraryId.Value, Guid.NewGuid(), DateTimeOffset.UtcNow);
                return (Guid)(await command.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ReadOnlyTrialException("idempotency_conflict");
        }

        return (await FindAsync(new DurableTaskId(id), cancellationToken).ConfigureAwait(false))!;
    }

    public ValueTask<ScanRequestRecord?> FindAsync(DurableTaskId taskId, CancellationToken token) =>
        ReadRequestAsync("WHERE request.task_id=$1", [taskId.Value], token);

    public ValueTask<ScanRequestRecord?> LatestAsync(LibraryId libraryId, CancellationToken token) =>
        ReadRequestAsync("WHERE request.library_id=$1 ORDER BY request.created_at DESC,request.task_id DESC LIMIT 1", [libraryId.Value], token);

    private ValueTask<ScanRequestRecord?> ReadRequestAsync(string predicate, object[] parameters, CancellationToken cancellationToken) =>
        database.RunAsync<ScanRequestRecord?>(async (connection, transaction, token) =>
        {
            await using var command = ModulePostgresSession.Command(connection, transaction,
                "SELECT " + Columns + " FROM scan_reconciliation.scan_request request " + predicate, parameters);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadRequest(reader) : null;
        }, cancellationToken);

    public ValueTask<IReadOnlyList<ScanRequestRecord>> RecoverBatchAsync(CancellationToken cancellationToken) =>
        database.RunAsync<IReadOnlyList<ScanRequestRecord>>(async (connection, transaction, token) =>
        {
            await using var command = ModulePostgresSession.Command(connection, transaction,
                "WITH candidates AS (SELECT task_id FROM scan_reconciliation.scan_request WHERE NOT terminal " +
                "ORDER BY recovered_at,task_id FOR UPDATE SKIP LOCKED LIMIT 32) UPDATE scan_reconciliation.scan_request request " +
                "SET recovered_at=clock_timestamp() FROM candidates WHERE request.task_id=candidates.task_id RETURNING " + Columns);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var result = new List<ScanRequestRecord>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(ReadRequest(reader));
            }

            return result;
        }, cancellationToken);

    public ValueTask MarkDispatchedAsync(DurableTaskId taskId, CancellationToken token) =>
        UpdateAsync("UPDATE scan_reconciliation.scan_request SET dispatched=true WHERE task_id=$1", [taskId.Value], token);

    public ValueTask MarkTerminalAsync(DurableTaskId taskId, CancellationToken token) =>
        UpdateAsync("UPDATE scan_reconciliation.scan_request SET terminal=true WHERE task_id=$1", [taskId.Value], token);

    public async ValueTask<bool> CancelAsync(LibraryId libraryId, DurableTaskId taskId, ManagementOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            return await database.RunAsync(async (connection, transaction, token) =>
            {
                await using var command = ModulePostgresSession.Command(connection, transaction,
                    "SELECT scan_reconciliation.request_scan_cancellation($1,$2,$3,$4,$5)",
                    operation.PrincipalId, operation.IdempotencyKey, libraryId.Value, taskId.Value, DateTimeOffset.UtcNow);
                return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ReadOnlyTrialException("idempotency_conflict");
        }
    }

    public ValueTask<IReadOnlyList<ScanRunRecord>> RunsAsync(DurableTaskId taskId, CancellationToken cancellationToken) =>
        database.RunAsync<IReadOnlyList<ScanRunRecord>>(async (connection, transaction, token) =>
        {
            await using var command = ModulePostgresSession.Command(connection, transaction,
                "SELECT scan_id,attempt,observed_entries,committed_entries,started_at,finished_at,failure_code " +
                "FROM scan_reconciliation.scan_run WHERE task_id=$1 ORDER BY attempt DESC LIMIT 100", taskId.Value);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var result = new List<ScanRunRecord>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(new ScanRunRecord(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                    reader.GetFieldValue<DateTimeOffset>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }

            return result;
        }, cancellationToken);

    public ValueTask StartRunAsync(Guid scanId, ScanRequestRecord request, int attempt, DateTimeOffset startedAt, CancellationToken token) =>
        UpdateAsync("INSERT INTO scan_reconciliation.scan_run(scan_id,library_id,scan_kind,status,started_at,task_id,attempt) " +
            "VALUES($1,$2,'initial_read_only','running',$3,$4,$5)", [scanId, request.LibraryId.Value, startedAt, request.TaskId.Value, attempt], token);

    public ValueTask ProgressAsync(Guid scanId, int count, CancellationToken token) =>
        UpdateAsync("UPDATE scan_reconciliation.scan_run SET observed_entries=greatest(observed_entries,$2) WHERE scan_id=$1 AND status='running'",
            [scanId, count], token);

    public async ValueTask FinishRunAsync(Guid scanId, ScanRunTerminalState state, int observed, int committed,
        string? failureCode, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        _ = await database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = ModulePostgresSession.Command(connection, transaction,
                "UPDATE scan_reconciliation.scan_run SET status=$2,observed_entries=$3,committed_entries=$4,finished_at=$5,failure_code=$6 " +
                "WHERE scan_id=$1 AND (status<>'completed' OR $2='completed')", scanId, State(state), observed, committed, finishedAt);
            ModulePostgresSession.OptionalText(command, failureCode);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask UpdateAsync(string sql, object[] parameters, CancellationToken cancellationToken)
    {
        _ = await database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = ModulePostgresSession.Command(connection, transaction, sql, parameters);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ScanRequestRecord ReadRequest(NpgsqlDataReader reader) =>
        new(new DurableTaskId(reader.GetGuid(0)), new LibraryId(reader.GetGuid(1)), reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5));

    private static string State(ScanRunTerminalState state) => state switch
    {
        ScanRunTerminalState.Completed => "completed",
        ScanRunTerminalState.Cancelled => "cancelled",
        ScanRunTerminalState.TimedOut => "timed_out",
        ScanRunTerminalState.DiscoveryFailed => "discovery_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
