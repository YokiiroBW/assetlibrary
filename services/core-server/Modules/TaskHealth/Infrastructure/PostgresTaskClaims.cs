using AssetLibrary.Infrastructure.Postgres;
using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.TaskHealth.Infrastructure;

internal sealed class PostgresTaskClaims(ModulePostgresSession database, TaskTypeName? taskTypeFilter)
{
    public ValueTask<IReadOnlyList<DurableTaskLease>> ClaimAsync(DurableTaskClaimRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.RunAsync<IReadOnlyList<DurableTaskLease>>(async (connection, transaction, token) =>
        {
            var sql = taskTypeFilter.HasValue
                ? "SELECT * FROM task_health.claim_durable_tasks_of_type($1,$2,$3,$4)"
                : "SELECT * FROM task_health.claim_durable_tasks($1,$2,$3)";
            await using var command = ModulePostgresSession.Command(connection, transaction, sql,
                request.Worker.Value, request.BatchSize, checked((int)request.LeaseDuration.TotalSeconds));
            if (taskTypeFilter is { } filter)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = filter.Value });
            }

            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var result = new List<DurableTaskLease>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(new DurableTaskLease(new DurableTaskId(reader.GetGuid(0)), new TaskTypeName(reader.GetString(1)),
                    new JsonObjectPayload(reader.GetString(2)), (TaskPriority)reader.GetInt16(3), reader.GetInt32(4), reader.GetInt32(5),
                    new TaskLeaseIdentity(request.Worker, new LeaseToken(reader.GetGuid(6)), reader.GetInt64(7)),
                    reader.GetFieldValue<DateTimeOffset>(8), reader.GetBoolean(9)));
            }

            return result;
        }, cancellationToken);

}
