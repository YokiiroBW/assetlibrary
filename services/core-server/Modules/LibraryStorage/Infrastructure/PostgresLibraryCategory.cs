using AssetLibrary.Infrastructure.Postgres;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.LibraryStorage.Infrastructure;

internal sealed class PostgresLibraryCategory(NpgsqlDataSource dataSource)
{
    private readonly ModulePostgresSession database = new(dataSource, ModuleDatabaseRole.LibraryStorage);

    public async ValueTask<LibraryCategory> UpdateAsync(LibraryCategoryUpdate request, ManagementOperation operation,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await database.RunAsync(async (connection, transaction, token) =>
            {
                await using var command = ModulePostgresSession.Command(connection, transaction,
                    "SELECT library_storage.update_library_category($1,$2,$3,$4,$5,$6)");
                foreach (var value in new object[] { operation.PrincipalId, operation.IdempotencyKey,
                    request.LibraryId.Value, LibraryCategories.ToWire(request.Category),
                    LibraryCategories.ToWire(request.ExpectedCategory), now })
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = value });
                }

                return LibraryCategories.Parse((string)(await command.ExecuteScalarAsync(token).ConfigureAwait(false))!);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ReadOnlyTrialException("idempotency_conflict");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            throw new ReadOnlyTrialException("library_not_found");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new ReadOnlyTrialException("state_conflict");
        }
    }
}
