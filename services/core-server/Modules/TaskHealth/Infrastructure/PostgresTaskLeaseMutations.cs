using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.TaskHealth.Infrastructure;

internal sealed class PostgresTaskLeaseMutations(TaskHealthDatabase database)
{
    public ValueTask<DurableTaskHeartbeatResult> HeartbeatAsync(DurableTaskHeartbeatRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = TaskHealthDatabase.Command(connection, transaction,
                "SELECT * FROM task_health.heartbeat_durable_task($1,$2,$3,$4,$5)", request.TaskId.Value,
                request.Identity.Owner.Value, request.Identity.Token.Value, request.Identity.Generation,
                checked((int)request.LeaseDuration.TotalSeconds));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? new DurableTaskHeartbeatResult(TaskLeaseMutationStatus.Accepted, reader.GetBoolean(0), reader.GetFieldValue<DateTimeOffset>(1))
                : new DurableTaskHeartbeatResult(TaskLeaseMutationStatus.NotCurrent, false, null);
        }, cancellationToken);

    public ValueTask<DurableTaskFinishResult> FinishAsync(DurableTaskFinishRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = TaskHealthDatabase.Command(connection, transaction,
                "SELECT task_health.finish_durable_task($1,$2,$3,$4,$5,$6,$7)::text", request.TaskId.Value,
                request.Identity.Owner.Value, request.Identity.Token.Value, request.Identity.Generation, Outcome(request.Kind));
            TaskHealthDatabase.OptionalText(command, request.FailureCode?.Value);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer,
                Value = request.RetryDelay is null ? DBNull.Value : checked((int)request.RetryDelay.Value.TotalSeconds) });
            var state = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            return state is string text
                ? new DurableTaskFinishResult(TaskLeaseMutationStatus.Accepted, ParseState(text))
                : new DurableTaskFinishResult(TaskLeaseMutationStatus.NotCurrent, null);
        }, cancellationToken);

    public ValueTask<DurableTaskCancellationResult> RequestCancellationAsync(DurableTaskId taskId, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = TaskHealthDatabase.Command(connection, transaction,
                "SELECT task_health.request_durable_task_cancellation($1)", taskId.Value);
            var result = (string)(await command.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            return new DurableTaskCancellationResult(result switch
            {
                "cancelled" => TaskCancellationStatus.Cancelled, "requested" => TaskCancellationStatus.Requested,
                "already_cancelled" => TaskCancellationStatus.AlreadyCancelled, "already_terminal" => TaskCancellationStatus.AlreadyTerminal,
                "not_found" => TaskCancellationStatus.NotFound, _ => throw new InvalidOperationException("Unknown task cancellation status."),
            });
        }, cancellationToken);

    internal static DurableTaskState ParseState(string state) => state switch
    {
        "queued" => DurableTaskState.Queued, "leased" => DurableTaskState.Leased,
        "succeeded" => DurableTaskState.Succeeded, "failed" => DurableTaskState.Failed,
        "cancelled" => DurableTaskState.Cancelled, _ => throw new InvalidOperationException("Unknown task state."),
    };

    private static string Outcome(DurableTaskFinishKind kind) => kind switch
    {
        DurableTaskFinishKind.Succeeded => "succeeded", DurableTaskFinishKind.RetryableFailure => "retryable_failure",
        DurableTaskFinishKind.PermanentFailure => "permanent_failure", DurableTaskFinishKind.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
