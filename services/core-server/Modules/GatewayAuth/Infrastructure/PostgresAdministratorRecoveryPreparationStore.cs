using AssetLibrary.Modules.GatewayAuth.Application;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PostgresAdministratorRecoveryPreparationStore(NpgsqlDataSource dataSource)
    : IAdministratorRecoveryPreparationStore
{
    private readonly NpgsqlDataSource dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public ValueTask<AdministratorRecoveryPreparationResult> PrepareRecoveryAsync(
        VerifiedOutOfBandAuthorization authorization,
        CancellationToken cancellationToken) =>
        PostgresAuthenticationExecutor.ExecuteAsync(
            dataSource,
            readOnly: true,
            async (connection, transaction, token) =>
            {
                await using var command = PostgresAuthenticationExecutor.Command(
                    connection,
                    transaction,
                    "SELECT outcome, expected_credential_version "
                    + "FROM gateway_auth.prepare_local_administrator_recovery($1, $2, $3, $4);");
                PostgresAuthenticationParameters.AddUuid(command, authorization.AuthorizationId);
                PostgresAuthenticationParameters.AddUuid(command, authorization.OperationId);
                PostgresAuthenticationParameters.AddText(command, authorization.TargetAccountName.Value);
                PostgresAuthenticationParameters.AddTimestampWithTimeZone(command, authorization.ExpiresAt);
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Administrator recovery preparation is unavailable.");
                }

                var outcome = reader.GetString(0) switch
                {
                    "ready" => AdministratorRecoveryPreparationOutcome.Ready,
                    "authorization_rejected" => AdministratorRecoveryPreparationOutcome.AuthorizationRejected,
                    "state_conflict" => AdministratorRecoveryPreparationOutcome.StateConflict,
                    "request_conflict" => AdministratorRecoveryPreparationOutcome.RequestConflict,
                    _ => throw new InvalidOperationException("Administrator recovery preparation is invalid."),
                };
                var version = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                if ((outcome == AdministratorRecoveryPreparationOutcome.Ready) != (version is > 0)
                    || await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Administrator recovery preparation is invalid.");
                }

                return new AdministratorRecoveryPreparationResult(outcome, version);
            },
            cancellationToken);
}
