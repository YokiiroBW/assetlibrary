using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresServiceReadCredentialStore(NpgsqlDataSource dataSource) : IServiceReadCredentialStore
{
    public ValueTask<bool> CreatePrincipalAsync(Guid principalId, string displayName,
        ServiceReadOperation operation, CancellationToken cancellationToken) =>
        MutateAsync("SELECT gateway_auth.create_service_read_principal($1,$2,$3,$4);", command =>
        {
            PostgresAuthenticationParameters.AddUuid(command, principalId);
            PostgresAuthenticationParameters.AddText(command, displayName);
            AddOperation(command, operation);
        }, cancellationToken);

    public async ValueTask<bool> IssueAsync(Guid principalId, Guid credentialId, Guid? replacedCredentialId,
        AuthenticationSecretDigest digest, DateTimeOffset expiresAt, ServiceReadOperation operation,
        CancellationToken cancellationToken)
    {
        var bytes = digest.Value.ToArray();
        try
        {
            return await MutateAsync("SELECT gateway_auth.issue_service_read_credential($1,$2,$3,$4,$5,$6,$7);", command =>
            {
                PostgresAuthenticationParameters.AddUuid(command, principalId);
                PostgresAuthenticationParameters.AddUuid(command, credentialId);
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)replacedCredentialId ?? DBNull.Value });
                PostgresAuthenticationParameters.AddBytes(command, bytes);
                PostgresAuthenticationParameters.AddTimestampWithTimeZone(command, expiresAt);
                AddOperation(command, operation);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async ValueTask<ServiceReadIdentity?> AuthenticateAsync(AuthenticationSecretDigest digest, CancellationToken cancellationToken)
    {
        var bytes = digest.Value.ToArray();
        try
        {
            return await PostgresAuthenticationExecutor.ExecuteAsync<ServiceReadIdentity?>(dataSource, true,
                async (connection, transaction, token) =>
                {
                    await using var command = PostgresAuthenticationExecutor.Command(connection, transaction,
                        "SELECT principal_id, subject_key FROM gateway_auth.authenticate_service_read_credential($1);");
                    PostgresAuthenticationParameters.AddBytes(command, bytes);
                    await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                    return await reader.ReadAsync(token).ConfigureAwait(false)
                        ? new ServiceReadIdentity(reader.GetGuid(0), new AuthenticatedSubject(reader.GetString(1))) : null;
                }, cancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public ValueTask<bool> RevokeAsync(Guid principalId, Guid credentialId,
        ServiceReadOperation operation, CancellationToken cancellationToken) =>
        MutateAsync("SELECT gateway_auth.revoke_service_read_credential($1,$2,$3,$4);", command =>
        {
            PostgresAuthenticationParameters.AddUuid(command, principalId);
            PostgresAuthenticationParameters.AddUuid(command, credentialId);
            AddOperation(command, operation);
        }, cancellationToken);

    public ValueTask<bool> DisableAsync(Guid principalId, ServiceReadOperation operation, CancellationToken cancellationToken) =>
        MutateAsync("SELECT gateway_auth.disable_service_read_principal($1,$2,$3);", command =>
        {
            PostgresAuthenticationParameters.AddUuid(command, principalId);
            AddOperation(command, operation);
        }, cancellationToken);

    private ValueTask<bool> MutateAsync(string sql, Action<NpgsqlCommand> parameters, CancellationToken cancellationToken) =>
        PostgresAuthenticationExecutor.ExecuteAsync(dataSource, false, async (connection, transaction, token) =>
        {
            await using var command = PostgresAuthenticationExecutor.Command(connection, transaction, sql);
            parameters(command);
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
        }, cancellationToken);

    private static void AddOperation(NpgsqlCommand command, ServiceReadOperation operation)
    {
        PostgresAuthenticationParameters.AddText(command, operation.OperatorId);
        PostgresAuthenticationParameters.AddUuid(command, operation.CorrelationId);
    }
}
