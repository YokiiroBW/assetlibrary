using AssetLibrary.Infrastructure.Postgres;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.LibraryStorage.Infrastructure;

public sealed class PostgresServiceLibraryReadGrantStore(NpgsqlDataSource dataSource) : IServiceLibraryReadGrantStore
{
    private readonly ModulePostgresSession database = new(dataSource, ModuleDatabaseRole.LibraryStorage);

    public async ValueTask SetAsync(ServiceLibraryReadGrantChange request, ServiceLibraryReadGrantOperation operation,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await database.RunAsync(async (connection, transaction, token) =>
            {
                await using var command = ModulePostgresSession.Command(connection, transaction,
                    "SELECT library_storage.set_service_read_grants($1,$2,$3,$4,$5,$6)",
                    request.PrincipalId, request.LibraryIds.Select(id => id.Value).ToArray(), request.Granted,
                    operation.OperatorId, operation.CorrelationId, now);
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            throw new ReadOnlyTrialException("library_not_found");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw new ReadOnlyTrialException("permission_denied");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ReadOnlyTrialException("idempotency_conflict");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new ReadOnlyTrialException("state_conflict");
        }
    }
}
