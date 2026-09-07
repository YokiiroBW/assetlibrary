using AssetLibrary.Infrastructure.Postgres;
using AssetLibrary.Modules.TaskHealth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.TaskHealth.Infrastructure;

public sealed class PostgresTaskExecution(NpgsqlDataSource dataSource) : IDurableTaskInspector, IDurableTaskCommitGuard
{
    private readonly ModulePostgresSession database = new(dataSource, ModuleDatabaseRole.TaskHealth);

    public ValueTask<DurableTaskDetails?> FindAsync(DurableTaskId taskId, CancellationToken cancellationToken) =>
        database.RunAsync<DurableTaskDetails?>(async (connection, transaction, token) =>
        {
            await using var command = ModulePostgresSession.Command(connection, transaction,
                "SELECT state::text,attempts,max_attempts,cancellation_requested_at IS NOT NULL,created_at,updated_at,last_failure_code " +
                "FROM task_health.durable_task WHERE task_id=$1", taskId.Value);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? new DurableTaskDetails(new DurableTaskSnapshot(taskId, PostgresTaskLeaseMutations.ParseState(reader.GetString(0)),
                    reader.GetInt32(1), reader.GetInt32(2), reader.GetBoolean(3)), reader.GetFieldValue<DateTimeOffset>(4),
                    reader.GetFieldValue<DateTimeOffset>(5), reader.IsDBNull(6) ? null : reader.GetString(6)) : null;
        }, cancellationToken);

    public ValueTask<bool> ReconcileCommittedAsync(DurableTaskId taskId, CancellationToken cancellationToken) =>
        database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = ModulePostgresSession.Command(connection, transaction,
                "SELECT task_health.reconcile_committed_task($1)", taskId.Value);
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
        }, cancellationToken);

    public ValueTask<int> CommitAsync(DurableTaskHeartbeatRequest request, Func<CancellationToken, ValueTask<int>> commit,
        CancellationToken cancellationToken) =>
        database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = TaskHealthLeaseSql.Command(connection, transaction,
                "SELECT task_health.lock_durable_task_commit($1,$2,$3,$4,$5)", request);
            var status = (string)(await command.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            if (status != "accepted")
            {
                throw new TaskCommitLeaseException(status == "cancelled" ? "scan_cancelled" : "scan_lease_lost");
            }

            return await commit(token).ConfigureAwait(false);
        }, cancellationToken, timeoutSeconds: 125);
}
