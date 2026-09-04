using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresLocalAccountLifecycleStore(NpgsqlDataSource dataSource)
    : ILocalAccountLifecycleStore
{
    private const string FindSql = """
        SELECT *
        FROM gateway_auth.read_local_account_for_administrator($1, $2, $3);
        """;
    private const string ProvisionSql = """
        SELECT *
        FROM gateway_auth.provision_local_account(
            $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11);
        """;
    private const string ReplaceCredentialSql = """
        SELECT *
        FROM gateway_auth.replace_local_account_credential(
            $1, $2, $3, $4, $5, $6, $7, $8, $9, $10);
        """;
    private const string SetEnabledSql = """
        SELECT *
        FROM gateway_auth.set_local_account_enabled($1, $2, $3, $4, $5, $6);
        """;
    private readonly NpgsqlDataSource dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public ValueTask<LocalAccountLifecycleResult> FindAsync(
        AuthenticatedIdentity actor,
        LocalAccountName accountName,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            FindSql,
            command =>
            {
                AddActor(command, actor);
                PostgresAuthenticationParameters.AddText(command, accountName.Value);
            },
            readOnly: true,
            cancellationToken);

    public ValueTask<LocalAccountLifecycleResult> ProvisionAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        Guid requestedPrincipalId,
        LocalAccountName accountName,
        LocalAccountDisplayName displayName,
        bool isSystemAdministrator,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken) =>
        ExecuteCredentialMutationAsync(
            ProvisionSql,
            credential,
            command =>
            {
                AddActor(command, actor);
                PostgresAuthenticationParameters.AddUuid(command, operationId);
                PostgresAuthenticationParameters.AddUuid(command, requestedPrincipalId);
                PostgresAuthenticationParameters.AddText(command, accountName.Value);
                PostgresAuthenticationParameters.AddText(command, displayName.Value);
                PostgresAuthenticationParameters.AddBoolean(command, isSystemAdministrator);
            },
            cancellationToken);

    public ValueTask<LocalAccountLifecycleResult> ReplaceCredentialAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedCredentialVersion,
        bool enableAccount,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken) =>
        ExecuteCredentialMutationAsync(
            ReplaceCredentialSql,
            credential,
            command =>
            {
                AddActor(command, actor);
                PostgresAuthenticationParameters.AddUuid(command, operationId);
                PostgresAuthenticationParameters.AddText(command, accountName.Value);
                PostgresAuthenticationParameters.AddBigint(command, expectedCredentialVersion);
                PostgresAuthenticationParameters.AddBoolean(command, enableAccount);
            },
            cancellationToken);

    public ValueTask<LocalAccountLifecycleResult> SetEnabledAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedPrincipalSessionVersion,
        bool enabled,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            SetEnabledSql,
            command =>
            {
                AddActor(command, actor);
                PostgresAuthenticationParameters.AddUuid(command, operationId);
                PostgresAuthenticationParameters.AddText(command, accountName.Value);
                PostgresAuthenticationParameters.AddBigint(
                    command,
                    expectedPrincipalSessionVersion);
                PostgresAuthenticationParameters.AddBoolean(command, enabled);
            },
            readOnly: false,
            cancellationToken);

    private async ValueTask<LocalAccountLifecycleResult> ExecuteCredentialMutationAsync(
        string sql,
        LocalCredentialEnrollmentMaterial credential,
        Action<NpgsqlCommand> addRequestParameters,
        CancellationToken cancellationToken)
    {
        var salt = credential.Salt.ToArray();
        var digest = credential.Digest.ToArray();
        try
        {
            return await ExecuteAsync(
                sql,
                command =>
                {
                    addRequestParameters(command);
                    PostgresAuthenticationParameters.AddText(command, credential.Algorithm);
                    PostgresAuthenticationParameters.AddInteger(command, credential.Iterations);
                    PostgresAuthenticationParameters.AddBytes(command, salt);
                    PostgresAuthenticationParameters.AddBytes(command, digest);
                },
                readOnly: false,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private ValueTask<LocalAccountLifecycleResult> ExecuteAsync(
        string sql,
        Action<NpgsqlCommand> addParameters,
        bool readOnly,
        CancellationToken cancellationToken) =>
        PostgresAuthenticationExecutor.ExecuteAsync(
            dataSource,
            readOnly,
            async (connection, transaction, token) =>
            {
                await using var command = PostgresAuthenticationExecutor.Command(
                    connection,
                    transaction,
                    sql);
                addParameters(command);
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "The local account lifecycle store returned no result.");
                }

                return ReadResult(reader);
            },
            cancellationToken);

    private static LocalAccountLifecycleResult ReadResult(NpgsqlDataReader reader)
    {
        var outcome = reader.GetString(0) switch
        {
            "applied" => LocalAccountLifecycleOutcome.Applied,
            "unauthorized" => LocalAccountLifecycleOutcome.Unauthorized,
            "account_conflict" => LocalAccountLifecycleOutcome.AccountConflict,
            "not_found" => LocalAccountLifecycleOutcome.NotFound,
            "state_conflict" => LocalAccountLifecycleOutcome.StateConflict,
            "last_administrator" => LocalAccountLifecycleOutcome.LastAdministrator,
            "operation_conflict" => LocalAccountLifecycleOutcome.OperationConflict,
            _ => throw new InvalidOperationException(
                "The local account lifecycle store returned an unknown outcome."),
        };
        var account = PostgresLocalAccountStateReader.Read(reader, 2);
        return new LocalAccountLifecycleResult(outcome, reader.GetBoolean(1), account);
    }

    private static void AddActor(NpgsqlCommand command, AuthenticatedIdentity actor)
    {
        PostgresAuthenticationParameters.AddUuid(command, actor.PrincipalId);
        PostgresAuthenticationParameters.AddBigint(command, actor.PrincipalSessionVersion);
    }
}
