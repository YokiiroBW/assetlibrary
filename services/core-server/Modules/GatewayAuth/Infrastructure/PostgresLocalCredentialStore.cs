using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresLocalCredentialStore(NpgsqlDataSource dataSource)
{
    private const string FindCredentialSql = """
        SELECT
            principal_id,
            subject_key,
            display_name,
            is_system_administrator,
            principal_session_version,
            secret_algorithm,
            secret_iterations,
            secret_salt,
            secret_digest,
            credential_version,
            can_attempt
        FROM gateway_auth.read_local_sign_in_material($1);
        """;
    private const string RecordFailureSql = """
        SELECT gateway_auth.record_local_sign_in_failure($1, $2);
        """;
    private readonly NpgsqlDataSource dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public ValueTask<LocalCredentialMaterial?> FindAsync(
        LocalAccountName accountName,
        CancellationToken cancellationToken) =>
        PostgresAuthenticationExecutor.ExecuteAsync(
            dataSource,
            readOnly: true,
            async (connection, transaction, token) =>
            {
                await using var command = PostgresAuthenticationExecutor.Command(
                    connection,
                    transaction,
                    FindCredentialSql);
                AddText(command, accountName.Value);
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    return null;
                }

                var identity = new AuthenticatedIdentity(
                    reader.GetGuid(0),
                    new AuthenticatedSubject(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetInt64(4),
                    PrimaryAuthenticationMethod.LocalAccount);
                var salt = reader.GetFieldValue<byte[]>(7);
                var digest = reader.GetFieldValue<byte[]>(8);
                try
                {
                    return new LocalCredentialMaterial(
                        new VerifiedPrimaryIdentity(identity, reader.GetInt64(9)),
                        reader.GetString(5),
                        reader.GetInt32(6),
                        salt,
                        digest,
                        reader.GetBoolean(10));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(salt);
                    CryptographicOperations.ZeroMemory(digest);
                }
            },
            cancellationToken);

    public async ValueTask RecordFailureAsync(
        LocalAccountName accountName,
        long observedCredentialVersion,
        CancellationToken cancellationToken)
    {
        _ = await PostgresAuthenticationExecutor.ExecuteAsync(
            dataSource,
            readOnly: false,
            async (connection, transaction, token) =>
            {
                await using var command = PostgresAuthenticationExecutor.Command(
                    connection,
                    transaction,
                    RecordFailureSql);
                AddText(command, accountName.Value);
                command.Parameters.Add(new NpgsqlParameter
                {
                    NpgsqlDbType = NpgsqlDbType.Bigint,
                    Value = observedCredentialVersion,
                });
                return await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void AddText(NpgsqlCommand command, string value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value,
        });
}
