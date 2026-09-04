using System.Data;
using Npgsql;

namespace AssetLibrary.CoreServer.Hosting;

internal interface IDatabaseReadinessProbe
{
    ValueTask<DatabaseReadinessResult> CheckAsync(CancellationToken cancellationToken);
}

internal sealed class PostgresDatabaseReadinessProbe : IDatabaseReadinessProbe, IAsyncDisposable
{
    private const string PermissionDeniedSqlState = "42501";
    private const string UndefinedTableSqlState = "42P01";
    private const string InvalidSchemaSqlState = "3F000";
    private const int MaximumRows = 1001;
    private readonly DatabaseMigrationContract contract;
    private readonly NpgsqlDataSource dataSource;

    private PostgresDatabaseReadinessProbe(
        DatabaseMigrationContract contract,
        NpgsqlDataSource dataSource)
    {
        this.contract = contract;
        this.dataSource = dataSource;
    }

    public static PostgresDatabaseReadinessProbe Create(
        DatabaseReadinessOptions options,
        DatabaseMigrationContract? contract = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = new NpgsqlDataSourceBuilder(options.Connection.Reveal());
        return new PostgresDatabaseReadinessProbe(
            contract ?? DatabaseMigrationContract.LoadCurrent(),
            builder.Build());
    }

    public async ValueTask<DatabaseReadinessResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(DatabaseReadinessOptions.TimeoutSeconds));
        try
        {
            var snapshot = await ReadSnapshotAsync(deadline.Token).ConfigureAwait(false);
            return DatabaseMigrationStateValidator.Validate(contract, snapshot);
        }
        catch (DatabaseAuditRoleMissingException)
        {
            return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.PermissionDenied);
        }
        catch (PostgresException exception) when (exception.SqlState == PermissionDeniedSqlState)
        {
            return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.PermissionDenied);
        }
        catch (PostgresException exception) when (
            exception.SqlState is UndefinedTableSqlState or InvalidSchemaSqlState)
        {
            return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.MigrationNotCurrent);
        }
        catch (Exception exception) when (exception is NpgsqlException
            or TimeoutException
            or IOException
            or InvalidOperationException)
        {
            return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.Unavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.Unavailable);
        }
    }

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();

    private async ValueTask<DatabaseMigrationSnapshot> ReadSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            "SET TRANSACTION READ ONLY;",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"SET LOCAL statement_timeout = '{DatabaseReadinessOptions.TimeoutSeconds}s';",
            cancellationToken).ConfigureAwait(false);

        var role = await ReadRoleBoundaryAsync(
            connection,
            transaction,
            cancellationToken).ConfigureAwait(false);
        if (!role.IsSafeAuditor)
        {
            throw new DatabaseAuditRoleMissingException();
        }

        await ExecuteAsync(
            connection,
            transaction,
            $"SET LOCAL ROLE {contract.AuditorRole};",
            cancellationToken).ConfigureAwait(false);
        var serverVersion = await ScalarAsync<int>(
            connection,
            transaction,
            "SELECT current_setting('server_version_num')::integer;",
            cancellationToken).ConfigureAwait(false);
        var bootstrap = await ReadBootstrapAsync(
            connection,
            transaction,
            cancellationToken).ConfigureAwait(false);
        var migrations = await ReadMigrationsAsync(
            connection,
            transaction,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new DatabaseMigrationSnapshot(
            serverVersion,
            bootstrap.FormatVersion,
            bootstrap.Checksum,
            migrations);
    }

    private static async ValueTask<RoleBoundary> ReadRoleBoundaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            transaction,
            """
            SELECT
                NOT rolinherit,
                NOT rolsuper,
                NOT rolcreatedb,
                NOT rolcreaterole,
                NOT rolreplication,
                NOT rolbypassrls,
                pg_has_role(current_user, 'assetlibrary_database_auditor', 'MEMBER')
            FROM pg_roles
            WHERE rolname = current_user;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new RoleBoundary(false);
        }

        var safe = Enumerable.Range(0, 7).All(reader.GetBoolean);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new RoleBoundary(false)
            : new RoleBoundary(safe);
    }

    private static async ValueTask<BootstrapState> ReadBootstrapAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            transaction,
            "SELECT format_version, checksum FROM migration.bootstrap_state WHERE singleton;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new BootstrapState(0, string.Empty);
        }

        var result = new BootstrapState(reader.GetInt32(0), reader.GetString(1).TrimEnd());
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new BootstrapState(0, string.Empty)
            : result;
    }

    private static async ValueTask<IReadOnlyList<AppliedDatabaseMigration>> ReadMigrationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            transaction,
            """
            SELECT version, name, module, owner_role, checksum
            FROM migration.ledger
            ORDER BY version
            LIMIT 1001;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var migrations = new List<AppliedDatabaseMigration>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (migrations.Count == MaximumRows)
            {
                throw new InvalidOperationException("The database migration ledger exceeded its row bound.");
            }

            migrations.Add(new AppliedDatabaseMigration(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4).TrimEnd()));
        }

        return migrations;
    }

    private static async ValueTask ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, sql);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is T result
            ? result
            : throw new InvalidOperationException("A database readiness scalar had an unexpected type.");
    }

    private static NpgsqlCommand Command(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql) =>
        new(sql, connection, transaction)
        {
            CommandTimeout = DatabaseReadinessOptions.TimeoutSeconds,
        };

    private sealed record BootstrapState(int FormatVersion, string Checksum);

    private sealed record RoleBoundary(bool IsSafeAuditor);

    private sealed class DatabaseAuditRoleMissingException : Exception
    {
    }
}
