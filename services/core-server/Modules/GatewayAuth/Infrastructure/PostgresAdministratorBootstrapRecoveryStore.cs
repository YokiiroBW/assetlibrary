using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresAdministratorBootstrapRecoveryStore(NpgsqlDataSource dataSource)
    : IAdministratorBootstrapRecoveryStore
{
    private const string BootstrapSql = """
        SELECT *
        FROM gateway_auth.bootstrap_first_local_administrator(
            $1, $2, $3, $4, $5, $6, $7, $8, $9, $10);
        """;
    private const string RecoverSql = """
        SELECT *
        FROM gateway_auth.recover_local_administrator(
            $1, $2, $3, $4, $5, $6, $7, $8, $9);
        """;
    private readonly NpgsqlDataSource dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public ValueTask<AdministratorBootstrapRecoveryResult> BootstrapAsync(
        VerifiedOutOfBandAuthorization authorization,
        Guid requestedPrincipalId,
        LocalAccountDisplayName displayName,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken) =>
        ExecuteCredentialMutationAsync(
            BootstrapSql,
            authorization,
            credential,
            command =>
            {
                PostgresAuthenticationParameters.AddUuid(command, requestedPrincipalId);
                PostgresAuthenticationParameters.AddText(command, displayName.Value);
            },
            cancellationToken);

    public ValueTask<AdministratorBootstrapRecoveryResult> RecoverAsync(
        VerifiedOutOfBandAuthorization authorization,
        long expectedCredentialVersion,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken) =>
        ExecuteCredentialMutationAsync(
            RecoverSql,
            authorization,
            credential,
            command => PostgresAuthenticationParameters.AddBigint(
                command,
                expectedCredentialVersion),
            cancellationToken);

    private async ValueTask<AdministratorBootstrapRecoveryResult> ExecuteCredentialMutationAsync(
        string sql,
        VerifiedOutOfBandAuthorization authorization,
        LocalCredentialEnrollmentMaterial credential,
        Action<NpgsqlCommand> addActionParameters,
        CancellationToken cancellationToken)
    {
        var salt = credential.Salt.ToArray();
        var digest = credential.Digest.ToArray();
        try
        {
            return await PostgresAuthenticationExecutor.ExecuteAsync(
                dataSource,
                readOnly: false,
                async (connection, transaction, token) =>
                {
                    await using var command = PostgresAuthenticationExecutor.Command(
                        connection,
                        transaction,
                        sql);
                    AddAuthorization(command, authorization);
                    addActionParameters(command);
                    PostgresAuthenticationParameters.AddText(command, credential.Algorithm);
                    PostgresAuthenticationParameters.AddInteger(command, credential.Iterations);
                    PostgresAuthenticationParameters.AddBytes(command, salt);
                    PostgresAuthenticationParameters.AddBytes(command, digest);
                    await using var reader = await command.ExecuteReaderAsync(token)
                        .ConfigureAwait(false);
                    if (!await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(
                            "The administrator bootstrap or recovery store returned no result.");
                    }

                    return ReadResult(reader);
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static void AddAuthorization(
        NpgsqlCommand command,
        VerifiedOutOfBandAuthorization authorization)
    {
        PostgresAuthenticationParameters.AddUuid(command, authorization.AuthorizationId);
        PostgresAuthenticationParameters.AddUuid(command, authorization.OperationId);
        PostgresAuthenticationParameters.AddText(command, authorization.TargetAccountName.Value);
        PostgresAuthenticationParameters.AddTimestampWithTimeZone(
            command,
            authorization.ExpiresAt);
    }

    private static AdministratorBootstrapRecoveryResult ReadResult(NpgsqlDataReader reader)
    {
        var outcome = reader.GetString(0) switch
        {
            "applied" => AdministratorBootstrapRecoveryOutcome.Applied,
            "authorization_rejected" =>
                AdministratorBootstrapRecoveryOutcome.AuthorizationRejected,
            "state_conflict" => AdministratorBootstrapRecoveryOutcome.StateConflict,
            "request_conflict" => AdministratorBootstrapRecoveryOutcome.RequestConflict,
            _ => throw new InvalidOperationException(
                "The administrator bootstrap or recovery store returned an unknown outcome."),
        };
        var account = PostgresLocalAccountStateReader.Read(reader, 2);
        return new AdministratorBootstrapRecoveryResult(
            outcome,
            reader.GetBoolean(1),
            account);
    }
}
