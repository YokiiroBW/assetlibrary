using System.Data;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PostgresAuthenticationExecutor
{
    public const int CommandTimeoutSeconds = 5;

    public static async ValueTask<TResult> ExecuteAsync<TResult>(
        NpgsqlDataSource dataSource,
        bool readOnly,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(operation);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken).ConfigureAwait(false);
        if (readOnly)
        {
            await ExecuteControlAsync(
                connection,
                transaction,
                "SET TRANSACTION READ ONLY;",
                cancellationToken).ConfigureAwait(false);
        }

        await ExecuteControlAsync(
            connection,
            transaction,
            "SET LOCAL ROLE assetlibrary_gateway_auth_runtime;",
            cancellationToken).ConfigureAwait(false);
        await ExecuteControlAsync(
            connection,
            transaction,
            "SET LOCAL statement_timeout = '5s';",
            cancellationToken).ConfigureAwait(false);
        var result = await operation(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static NpgsqlCommand Command(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandText) =>
        new(commandText, connection, transaction)
        {
            CommandTimeout = CommandTimeoutSeconds,
        };

    private static async ValueTask ExecuteControlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, commandText);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
