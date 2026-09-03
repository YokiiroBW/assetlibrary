using System.Data;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PostgresReadExecutor
{
    public static async ValueTask<List<T>> ReadAsync<T>(
        NpgsqlDataSource dataSource,
        string commandText,
        TimeSpan timeout,
        int maximumRows,
        Action<NpgsqlCommand> addParameters,
        Func<NpgsqlDataReader, T> readRow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(addParameters);
        ArgumentNullException.ThrowIfNull(readRow);
        if (maximumRows is < 1 or > ReadPageOptions.MaximumPageSize + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await BeginReadOnlyAsync(
            connection,
            timeout,
            cancellationToken).ConfigureAwait(false);
        await using var command = PostgresReadCommand.Create(
            connection,
            transaction,
            commandText,
            timeout);
        addParameters(command);

        var rows = new List<T>(maximumRows);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (rows.Count == maximumRows)
                {
                    throw new InvalidOperationException("A PostgreSQL read exceeded its row bound.");
                }

                rows.Add(readRow(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public static async ValueTask<NpgsqlTransaction> BeginReadOnlyAsync(
        NpgsqlConnection connection,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = PostgresReadCommand.Create(
                connection,
                transaction,
                "SET TRANSACTION READ ONLY;",
                timeout);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
