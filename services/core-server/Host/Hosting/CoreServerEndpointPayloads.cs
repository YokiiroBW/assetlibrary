namespace AssetLibrary.CoreServer.Hosting;

internal sealed record CoreServerHealthPayload(string Status, string Contract);

internal sealed record CoreServerReadyPayload(
    string Status,
    string Contract,
    string Scope,
    bool BusinessApiReady,
    bool ProductionFileWritesEnabled,
    bool? DatabaseReady = null,
    string? DatabaseContract = null,
    int? DatabaseSchemaVersion = null,
    string? Reason = null);

internal static class CoreServerEndpointPayloads
{
    public static CoreServerHealthPayload Health() =>
        new("ok", CoreServerBuildInfo.CurrentContract);

    public static CoreServerReadyPayload Ready() =>
        new(
            "ready",
            CoreServerBuildInfo.CurrentContract,
            "host_only",
            BusinessApiReady: false,
            ProductionFileWritesEnabled: false);

    public static CoreServerReadyPayload Database(DatabaseReadinessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new CoreServerReadyPayload(
            result.IsReady ? "ready" : "not_ready",
            CoreServerBuildInfo.CurrentContract,
            "host_database",
            BusinessApiReady: false,
            ProductionFileWritesEnabled: false,
            DatabaseReady: result.IsReady,
            DatabaseContract: DatabaseMigrationContract.Contract,
            DatabaseSchemaVersion: result.SchemaVersion,
            Reason: result.IsReady ? null : "database_not_ready");
    }
}
