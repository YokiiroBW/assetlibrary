using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresBrowserSessionStore(NpgsqlDataSource dataSource)
{
    private const string CreateSessionSql = """
        SELECT outcome, issued_at, idle_expires_at, absolute_expires_at
        FROM gateway_auth.create_browser_session($1, $2, $3, $4, $5, $6);
        """;
    private const string AuthenticateSessionSql = """
        SELECT
            principal_id,
            subject_key,
            display_name,
            is_system_administrator,
            principal_session_version,
            authentication_method
        FROM gateway_auth.authenticate_browser_session($1, $2, $3);
        """;
    private const string RevokeSessionSql = """
        SELECT gateway_auth.revoke_browser_session($1, $2);
        """;
    private readonly NpgsqlDataSource dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public ValueTask<BrowserSessionCreateResult> CreateAsync(
        VerifiedPrimaryIdentity identity,
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest csrfDigest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(sessionDigest);
        ArgumentNullException.ThrowIfNull(csrfDigest);
        return PostgresAuthenticationExecutor.ExecuteAsync(
            dataSource,
            readOnly: false,
            async (connection, transaction, token) =>
            {
                await using var command = PostgresAuthenticationExecutor.Command(
                    connection,
                    transaction,
                    CreateSessionSql);
                AddUuid(command, identity.Identity.PrincipalId);
                AddBigint(command, identity.Identity.PrincipalSessionVersion);
                AddText(command, Method(identity.Identity.AuthenticationMethod));
                AddNullableBigint(command, identity.LocalCredentialVersion);
                AddBytea(command, sessionDigest.Value);
                AddBytea(command, csrfDigest.Value);
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("The browser session store returned no outcome.");
                }

                return reader.GetString(0) switch
                {
                    "created" => BrowserSessionCreateResult.Created(
                        UtcTimestamp(reader, 1),
                        UtcTimestamp(reader, 2),
                        UtcTimestamp(reader, 3)),
                    "identity_stale" => BrowserSessionCreateResult.IdentityStale(),
                    "token_conflict" => BrowserSessionCreateResult.TokenConflict(),
                    _ => throw new InvalidOperationException(
                        "The browser session store returned an unknown outcome."),
                };
            },
            cancellationToken);
    }

    public ValueTask<AuthenticatedIdentity?> AuthenticateAsync(
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest? csrfDigest,
        bool requireCsrf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionDigest);
        if (requireCsrf != (csrfDigest is not null))
        {
            throw new ArgumentException("The CSRF requirement and digest are inconsistent.");
        }

        return PostgresAuthenticationExecutor.ExecuteAsync(
            dataSource,
            readOnly: false,
            async (connection, transaction, token) =>
            {
                await using var command = PostgresAuthenticationExecutor.Command(
                    connection,
                    transaction,
                    AuthenticateSessionSql);
                AddBytea(command, sessionDigest.Value);
                AddNullableBytea(command, csrfDigest?.Value);
                AddBoolean(command, requireCsrf);
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    return null;
                }

                return new AuthenticatedIdentity(
                    reader.GetGuid(0),
                    new AuthenticatedSubject(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetInt64(4),
                    Method(reader.GetString(5)));
            },
            cancellationToken);
    }

    public ValueTask<bool> RevokeAsync(
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest csrfDigest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionDigest);
        ArgumentNullException.ThrowIfNull(csrfDigest);
        return PostgresAuthenticationExecutor.ExecuteAsync(
            dataSource,
            readOnly: false,
            async (connection, transaction, token) =>
            {
                await using var command = PostgresAuthenticationExecutor.Command(
                    connection,
                    transaction,
                    RevokeSessionSql);
                AddBytea(command, sessionDigest.Value);
                AddBytea(command, csrfDigest.Value);
                return (bool)(await command.ExecuteScalarAsync(token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The browser session store returned no result."));
            },
            cancellationToken);
    }

    private static DateTimeOffset UtcTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static string Method(PrimaryAuthenticationMethod method) => method switch
    {
        PrimaryAuthenticationMethod.LocalAccount => "local",
        PrimaryAuthenticationMethod.Oidc => "oidc",
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    private static PrimaryAuthenticationMethod Method(string method) => method switch
    {
        "local" => PrimaryAuthenticationMethod.LocalAccount,
        "oidc" => PrimaryAuthenticationMethod.Oidc,
        _ => throw new InvalidOperationException(
            "The session contains an unknown authentication method."),
    };

    private static void AddText(NpgsqlCommand command, string value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value,
        });

    private static void AddUuid(NpgsqlCommand command, Guid value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = value,
        });

    private static void AddBigint(NpgsqlCommand command, long value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Bigint,
            Value = value,
        });

    private static void AddNullableBigint(NpgsqlCommand command, long? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Bigint,
            Value = value.HasValue ? value.Value : DBNull.Value,
        });

    private static void AddBytea(NpgsqlCommand command, ReadOnlyMemory<byte> value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Bytea,
            Value = value,
        });

    private static void AddNullableBytea(
        NpgsqlCommand command,
        ReadOnlyMemory<byte>? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Bytea,
            Value = value.HasValue ? value.Value : DBNull.Value,
        });

    private static void AddBoolean(NpgsqlCommand command, bool value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Boolean,
            Value = value,
        });
}
