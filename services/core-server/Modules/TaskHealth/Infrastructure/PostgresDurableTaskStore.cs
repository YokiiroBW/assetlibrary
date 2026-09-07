using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.TaskHealth.Infrastructure;

public sealed class PostgresDurableTaskStore(NpgsqlDataSource dataSource, TaskTypeName? taskTypeFilter = null) : IDurableTaskStore
{
    private readonly TaskHealthDatabase database = new(dataSource);

    public ValueTask<DurableTaskEnqueueResult> EnqueueAsync(DurableTaskEnqueueRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = TaskHealthDatabase.Command(connection, transaction,
                "SELECT task_id,created FROM task_health.enqueue_durable_task($1,$2,$3,$4,$5,$6,$7,$8)",
                request.TaskId.Value, request.IdempotencyKey.Value, request.TaskType.Value);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = request.Payload.Value });
            foreach (var value in new object[] { (short)request.Priority, request.MaxAttempts, request.NotBefore ?? now, now })
            {
                command.Parameters.Add(new NpgsqlParameter { Value = value });
            }

            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Task enqueue returned no result.");
            }

            return new DurableTaskEnqueueResult(new DurableTaskId(reader.GetGuid(0)),
                reader.GetBoolean(1) ? DurableTaskEnqueueStatus.Created : DurableTaskEnqueueStatus.Existing);
        }, cancellationToken);

    private readonly PostgresTaskClaims claims = new(new TaskHealthDatabase(dataSource), taskTypeFilter);
    public ValueTask<IReadOnlyList<DurableTaskLease>> ClaimAsync(DurableTaskClaimRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
        claims.ClaimAsync(request, now, cancellationToken);

    private readonly PostgresTaskLeaseMutations mutations = new(new TaskHealthDatabase(dataSource));
    public ValueTask<DurableTaskHeartbeatResult> HeartbeatAsync(DurableTaskHeartbeatRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
        mutations.HeartbeatAsync(request, now, cancellationToken);
    public ValueTask<DurableTaskFinishResult> FinishAsync(DurableTaskFinishRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
        mutations.FinishAsync(request, now, cancellationToken);
    public ValueTask<DurableTaskCancellationResult> RequestCancellationAsync(DurableTaskId taskId, DateTimeOffset now, CancellationToken cancellationToken) =>
        mutations.RequestCancellationAsync(taskId, now, cancellationToken);

    public ValueTask<int> ReclaimExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
        database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = TaskHealthDatabase.Command(connection, transaction,
                "SELECT task_health.reclaim_expired_durable_tasks($1)", batchSize);
            return (int)(await command.ExecuteScalarAsync(token).ConfigureAwait(false))!;
        }, cancellationToken);

}
