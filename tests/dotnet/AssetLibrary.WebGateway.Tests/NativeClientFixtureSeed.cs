using System.Text;
using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.WebGateway.Tests;

internal static class NativeClientFixtureSeed
{
    public static async Task<Guid> PrepareAsync(TrialHostIntegrationFixture host, NativeClientSampleAssets assets)
    {
        var keyResult = await TrialAdministratorOperator.ExecuteAsync("initialize-key", host.Runtime, Stream.Null, CancellationToken.None);
        Assert.AreEqual(0, keyResult.ExitCode, "Test operator initialization failed.");
        var bootstrap = new JsonObject
        {
            ["account_name"] = "trial-admin",
            ["display_name"] = "原生客户端测试管理员",
            ["password"] = TrialHostIntegrationAuthentication.Password,
            ["operation_id"] = Guid.NewGuid().ToString("D"),
            ["authorization_id"] = Guid.NewGuid().ToString("D"),
            ["expires_at"] = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
        };
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(bootstrap.ToJsonString())))
        {
            var initialized = await TrialAdministratorOperator.ExecuteAsync("bootstrap", host.Runtime, input, CancellationToken.None);
            Assert.AreEqual(0, initialized.ExitCode, "Test administrator bootstrap failed.");
        }

        await host.StartAsync();
        var administrator = await TrialHostIntegrationHttp.SignInAsync(host);
        await TrialHostIntegrationAccounts.ProvisionReaderAsync(host, administrator);
        var registration = await TrialHostIntegrationHttp.ControlAsync(host, administrator, "libraries.register", new JsonObject
        {
            ["root_path"] = assets.LibraryRoot,
            ["display_name"] = "原生客户端隔离样例",
            ["source_key"] = "fixtures",
            ["category"] = "images",
        }, Guid.NewGuid());
        Assert.AreEqual(200, registration.Status, "Synthetic library registration failed.");
        var libraryId = registration.Payload["body"]!["library_id"]!.GetValue<Guid>();
        var libraryBody = new JsonObject { ["library_id"] = libraryId.ToString("D") };
        var started = await TrialHostIntegrationHttp.ControlAsync(host, administrator, "library_scans.start", libraryBody, Guid.NewGuid());
        Assert.AreEqual(200, started.Status, "Synthetic initial scan was rejected.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            var progress = await TrialHostIntegrationHttp.ControlAsync(host, administrator, "library_scans.get", libraryBody);
            Assert.AreEqual(200, progress.Status, "Synthetic scan status failed.");
            var state = progress.Payload["body"]!["scan"]!["state"]!.GetValue<string>();
            if (state == "succeeded") break;
            Assert.IsTrue(state is "queued" or "leased", "Synthetic initial scan failed.");
            await Task.Delay(100, deadline.Token);
        }

        assets.VerifyUnchanged();
        return libraryId;
    }
}
