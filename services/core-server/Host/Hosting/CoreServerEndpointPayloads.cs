namespace AssetLibrary.CoreServer.Hosting;

internal sealed record CoreServerHealthPayload(string Status, string Contract);

internal sealed record CoreServerReadyPayload(
    string Status,
    string Contract,
    string Scope,
    bool BusinessApiReady,
    bool ProductionFileWritesEnabled);

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
}
