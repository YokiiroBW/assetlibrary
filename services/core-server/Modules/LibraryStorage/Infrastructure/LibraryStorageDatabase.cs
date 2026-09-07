using Npgsql;

namespace AssetLibrary.Modules.LibraryStorage.Infrastructure;

internal sealed class LibraryStorageDatabase(NpgsqlDataSource source)
{
    public async ValueTask<T> RunAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, ValueTask<T>> operation, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await using var connection = await source.OpenConnectionAsync(deadline.Token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(deadline.Token).ConfigureAwait(false);
        await using (var setup = Command(connection, transaction,
            "SET LOCAL ROLE assetlibrary_library_storage_runtime; SET LOCAL statement_timeout = '5s'; SET LOCAL lock_timeout = '4s';"))
        {
            await setup.ExecuteNonQueryAsync(deadline.Token).ConfigureAwait(false);
        }

        var result = await operation(connection, transaction, deadline.Token).ConfigureAwait(false);
        await transaction.CommitAsync(deadline.Token).ConfigureAwait(false);
        return result;
    }

    public static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 5 };
}
