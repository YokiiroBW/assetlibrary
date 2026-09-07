using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.TaskHealth.Infrastructure;

internal sealed class TaskHealthDatabase(NpgsqlDataSource source)
{
    public async ValueTask<T> RunAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, ValueTask<T>> operation,
        CancellationToken token, int timeoutSeconds = 5)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds + 5));
        await using var connection = await source.OpenConnectionAsync(deadline.Token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(deadline.Token).ConfigureAwait(false);
        await using (var setup = Command(connection, transaction,
            "SET LOCAL ROLE assetlibrary_task_health_runtime; SET LOCAL statement_timeout='5s'; SET LOCAL lock_timeout='4s';"))
        {
            await setup.ExecuteNonQueryAsync(deadline.Token).ConfigureAwait(false);
        }

        var result = await operation(connection, transaction, deadline.Token).ConfigureAwait(false);
        await transaction.CommitAsync(deadline.Token).ConfigureAwait(false);
        return result;
    }

    public static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params object[] values)
    {
        var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 5 };
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return command;
    }

    public static void OptionalText(NpgsqlCommand command, string? value) =>
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = value is null ? DBNull.Value : value });
}
