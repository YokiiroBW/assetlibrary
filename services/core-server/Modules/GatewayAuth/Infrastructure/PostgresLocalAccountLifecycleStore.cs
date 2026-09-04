using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;
using NpgsqlTypes;

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
                AddText(command, accountName.Value);
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
                AddUuid(command, operationId);
                AddUuid(command, requestedPrincipalId);
                AddText(command, accountName.Value);
                AddText(command, displayName.Value);
                AddBoolean(command, isSystemAdministrator);
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
                AddUuid(command, operationId);
                AddText(command, accountName.Value);
                AddBigint(command, expectedCredentialVersion);
                AddBoolean(command, enableAccount);
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
                AddUuid(command, operationId);
                AddText(command, accountName.Value);
                AddBigint(command, expectedPrincipalSessionVersion);
                AddBoolean(command, enabled);
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
                    AddText(command, credential.Algorithm);
                    AddInteger(command, credential.Iterations);
                    AddBytes(command, salt);
                    AddBytes(command, digest);
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
        LocalAccountState? account = null;
        if (!reader.IsDBNull(2))
        {
            account = new LocalAccountState(
                reader.GetGuid(2),
                new LocalAccountName(reader.GetString(3)),
                new LocalAccountDisplayName(reader.GetString(4)),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetInt64(7),
                reader.GetInt64(8));
        }

        return new LocalAccountLifecycleResult(outcome, reader.GetBoolean(1), account);
    }

    private static void AddActor(NpgsqlCommand command, AuthenticatedIdentity actor)
    {
        AddUuid(command, actor.PrincipalId);
        AddBigint(command, actor.PrincipalSessionVersion);
    }

    private static void AddUuid(NpgsqlCommand command, Guid value) =>
        Add(command, NpgsqlDbType.Uuid, value);

    private static void AddText(NpgsqlCommand command, string value) =>
        Add(command, NpgsqlDbType.Text, value);

    private static void AddBoolean(NpgsqlCommand command, bool value) =>
        Add(command, NpgsqlDbType.Boolean, value);

    private static void AddInteger(NpgsqlCommand command, int value) =>
        Add(command, NpgsqlDbType.Integer, value);

    private static void AddBigint(NpgsqlCommand command, long value) =>
        Add(command, NpgsqlDbType.Bigint, value);

    private static void AddBytes(NpgsqlCommand command, byte[] value) =>
        Add(command, NpgsqlDbType.Bytea, value);

    private static void Add(NpgsqlCommand command, NpgsqlDbType type, object value) =>
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value });
}
