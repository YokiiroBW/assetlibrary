namespace AssetLibrary.CoreServer.Hosting;

internal sealed record DatabaseMigrationSnapshot(
    int ServerVersionNumber,
    int BootstrapFormatVersion,
    string BootstrapChecksum,
    IReadOnlyList<AppliedDatabaseMigration> Migrations);

internal sealed record AppliedDatabaseMigration(
    int Version,
    string Name,
    string Module,
    string OwnerRole,
    string Checksum);

internal enum DatabaseReadinessStatus
{
    Ready = 0,
    Unavailable = 1,
    PermissionDenied = 2,
    ServerVersionMismatch = 3,
    BootstrapInvalid = 4,
    MigrationNotCurrent = 5,
    MigrationDrift = 6,
}

internal sealed record DatabaseReadinessResult(
    DatabaseReadinessStatus Status,
    int? SchemaVersion = null)
{
    public bool IsReady => Status == DatabaseReadinessStatus.Ready;

    public string PublicCode => Status switch
    {
        DatabaseReadinessStatus.Ready => "ready",
        DatabaseReadinessStatus.Unavailable => "database_unavailable",
        DatabaseReadinessStatus.PermissionDenied => "database_permission_denied",
        DatabaseReadinessStatus.ServerVersionMismatch => "database_version_mismatch",
        DatabaseReadinessStatus.BootstrapInvalid => "database_bootstrap_invalid",
        DatabaseReadinessStatus.MigrationNotCurrent => "database_migration_not_current",
        DatabaseReadinessStatus.MigrationDrift => "database_migration_drift",
        _ => "database_unavailable",
    };

    public static DatabaseReadinessResult Ready(int schemaVersion) =>
        new(DatabaseReadinessStatus.Ready, schemaVersion);

    public static DatabaseReadinessResult NotReady(DatabaseReadinessStatus status)
    {
        if (status == DatabaseReadinessStatus.Ready)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new DatabaseReadinessResult(status);
    }
}

internal static class DatabaseMigrationStateValidator
{
    public static DatabaseReadinessResult Validate(
        DatabaseMigrationContract contract,
        DatabaseMigrationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ServerVersionNumber / 10000 != contract.PostgreSqlMajor)
        {
            return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.ServerVersionMismatch);
        }

        if (snapshot.BootstrapFormatVersion != 1
            || !string.Equals(
                snapshot.BootstrapChecksum,
                contract.BootstrapChecksum,
                StringComparison.Ordinal))
        {
            return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.BootstrapInvalid);
        }

        if (snapshot.Migrations.Count != contract.Migrations.Count)
        {
            return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.MigrationNotCurrent);
        }

        for (var index = 0; index < contract.Migrations.Count; index++)
        {
            var expected = contract.Migrations[index];
            var actual = snapshot.Migrations[index];
            if (actual.Version != expected.Version
                || !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal)
                || !string.Equals(actual.Module, expected.Module, StringComparison.Ordinal)
                || !string.Equals(actual.OwnerRole, expected.OwnerRole, StringComparison.Ordinal)
                || !string.Equals(actual.Checksum, expected.Checksum, StringComparison.Ordinal))
            {
                return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.MigrationDrift);
            }
        }

        return DatabaseReadinessResult.Ready(contract.LatestVersion);
    }
}
