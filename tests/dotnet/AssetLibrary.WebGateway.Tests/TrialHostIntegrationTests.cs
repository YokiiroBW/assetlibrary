using System.Text.Json;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class TrialHostIntegrationTests
{
    [TestMethod]
    public async Task RealBrowserCreatesScansSearchesAndResumesAnUnmodifiedPhysicalLibrary()
    {
        var settings = TrialHostIntegrationSettings.Load();
        var assets = new TrialHostIntegrationAssets(settings.RuntimeRoot);
        await using var host = await TrialHostIntegrationFixture.CreateAsync(settings);
        await TrialHostIntegrationReadiness.AssertWrongModuleLoginFailsAsync(host);
        var key = await TrialAdministratorOperator.ExecuteAsync("initialize-key", host.Runtime, Stream.Null, CancellationToken.None);
        Assert.AreEqual(0, key.ExitCode, "Initialize protected operator key");
        var bootstrapJson = JsonSerializer.SerializeToUtf8Bytes(new
        {
            authorization_id = Guid.NewGuid().ToString("D"),
            operation_id = Guid.NewGuid().ToString("D"),
            account_name = "trial-admin",
            display_name = "试用管理员",
            expires_at = DateTimeOffset.UtcNow.AddMinutes(5),
            password = TrialHostIntegrationAuthentication.Password,
        });
        using var bootstrapInput = new MemoryStream(bootstrapJson);
        var administrator = await TrialAdministratorOperator.ExecuteAsync("bootstrap", host.Runtime, bootstrapInput, CancellationToken.None);
        Assert.AreEqual(0, administrator.ExitCode, administrator.Json);
        await TrialHostIntegrationReadModel.AssertEmptyAsync(host.Services);
        await host.StartAsync();
        var libraryId = await TrialHostIntegrationBrowser.RunAsync(host, settings, assets, "initial");
        assets.AssertUnchanged();
        var session = await TrialHostIntegrationHttp.SignInAsync(host);
        await TrialHostIntegrationAuthorization.AssertBoundariesAsync(host, session, libraryId);
        await host.RestartAsync();
        var resumed = await TrialHostIntegrationHttp.SendAsync(host, session, HttpMethod.Get, "/assetlink/v1/auth/session");
        Assert.AreEqual(200, resumed.Status, "Server-side session and protected Cookie survive restart.");
        host.Authentication.Risk.Available = false;
        Assert.AreEqual(libraryId, await TrialHostIntegrationBrowser.RunAsync(host, settings, assets, "resumed", libraryId));
        await TrialHostIntegrationStorage.AssertOfflineSnapshotAsync(host, assets, session, libraryId);
        await TrialHostIntegrationStorage.AssertFailureRetryAsync(host, assets, session);
        await TrialHostIntegrationStorage.AssertQueuedCancellationAsync(host, assets, session);
        await TrialHostIntegrationRecovery.AssertRecoveryAsync(host, session);
        assets.AssertUnchanged();
    }
}
