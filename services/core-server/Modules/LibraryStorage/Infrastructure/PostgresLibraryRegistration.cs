using AssetLibrary.Infrastructure.Postgres;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.LibraryStorage.Infrastructure;

internal sealed class PostgresLibraryRegistration(NpgsqlDataSource dataSource)
{
    private readonly ModulePostgresSession database = new(dataSource, ModuleDatabaseRole.LibraryStorage);

    public async ValueTask<LibraryId?> FindRegistrationAsync(LibraryRegistrationRequest request, ManagementOperation operation, CancellationToken token)
    {
        try
        {
            return await database.RunAsync<LibraryId?>(async (connection, transaction, ct) =>
            {
                await using var command = ModulePostgresSession.Command(connection, transaction,
                    "SELECT library_storage.find_registration_operation($1,$2,$3)");
                RegistrationParameters(command, request, operation);
                return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is Guid id ? new LibraryId(id) : null;
            }, token).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ReadOnlyTrialException("idempotency_conflict");
        }
    }

    public async ValueTask<LibraryId> RegisterAsync(ConfiguredStorageSource source, LibraryRegistrationRequest request,
        CanonicalLibraryRoot root, ManagementOperation operation, DateTimeOffset now, CancellationToken token)
    {
        try
        {
            return await database.RunAsync(async (connection, transaction, ct) =>
            {
                await using var command = ModulePostgresSession.Command(connection, transaction,
                    "SELECT library_storage.register_trial_library_v2($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)");
                RegistrationParameters(command, request, operation);
                foreach (var value in new object[] { Guid.NewGuid(), source.StorageSourceId.Value, source.DisplayName,
                    source.AllowedRoot.Comparison == RootPathComparison.CaseSensitive, request.DisplayName, root.Value, now })
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = value });
                }

                command.Parameters.Add(new NpgsqlParameter { Value = LibraryCategories.ToWire(request.Category) });

                return new LibraryId((Guid)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
            }, token).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ReadOnlyTrialException(exception.ConstraintName == "registration_idempotency" ? "idempotency_conflict" : "root_overlap");
        }
    }

    private static void RegistrationParameters(NpgsqlCommand command, LibraryRegistrationRequest request, ManagementOperation operation)
    {
        command.Parameters.Add(new NpgsqlParameter { Value = operation.PrincipalId });
        command.Parameters.Add(new NpgsqlParameter { Value = operation.IdempotencyKey });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(request, LibraryRegistrationJsonContext.Default.LibraryRegistrationRequest)
        });
    }
}

[JsonSerializable(typeof(LibraryRegistrationRequest))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
internal sealed partial class LibraryRegistrationJsonContext : JsonSerializerContext;
