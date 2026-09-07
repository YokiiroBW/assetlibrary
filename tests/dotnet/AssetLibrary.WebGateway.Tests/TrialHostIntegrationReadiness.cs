using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.CoreServer.Hosting.Trial;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationReadiness
{
    public static async Task AssertReadyAsync(PostgresDatabaseReadinessProbe migrations, TrialDatabaseConnections connections)
    {
        var result = await new TrialRuntimeReadinessProbe(migrations, connections).CheckAsync(CancellationToken.None);
        Assert.IsTrue(result.IsReady, "Real database and module-role readiness");
    }

    public static async Task AssertWrongModuleLoginFailsAsync(TrialHostIntegrationFixture host)
    {
        var actual = host.Configuration.Database;
        var swapped = actual with
        {
            GatewayConnectionFile = actual.AssetConnectionFile,
            AssetConnectionFile = actual.GatewayConnectionFile,
        };
        await using var connections = await TrialDatabaseConnections.CreateAsync(swapped, CancellationToken.None);
        await using var audit = PostgresDatabaseReadinessProbe.Create(connections.Audit);
        var result = await new TrialRuntimeReadinessProbe(audit, connections).CheckAsync(CancellationToken.None);
        Assert.IsFalse(result.IsReady, "A valid migration ledger cannot authorize the wrong runtime LOGIN.");
    }
}
