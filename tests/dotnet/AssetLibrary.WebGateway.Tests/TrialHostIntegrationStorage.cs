using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationStorage
{
    public static async Task AssertOfflineSnapshotAsync(TrialHostIntegrationFixture host,
        TrialHostIntegrationAssets assets, TrialHostIntegrationSession session, Guid libraryId)
    {
        var availability = host.Services.GetRequiredService<ILibraryAvailability>();
        assets.SetOffline(assets.LibraryRoot, true);
        try
        {
            Assert.AreEqual(StorageAvailability.Offline, await availability.RefreshAsync(new LibraryId(libraryId), CancellationToken.None));
            var last = await TrialHostIntegrationHttp.ControlAsync(host, session, "entries.browse", Body(libraryId, browse: true));
            Assert.AreEqual(200, last.Status);
            Assert.AreEqual("offline", last.Payload["body"]!["library"]!["availability"]!.GetValue<string>());
            Assert.HasCount(2, last.Payload["body"]!["items"]!.AsArray());
        }
        finally
        {
            assets.SetOffline(assets.LibraryRoot, false);
        }

        Assert.AreEqual(StorageAvailability.Online, await availability.RefreshAsync(new LibraryId(libraryId), CancellationToken.None));
    }

    public static async Task AssertFailureRetryAsync(TrialHostIntegrationFixture host,
        TrialHostIntegrationAssets assets, TrialHostIntegrationSession session)
    {
        await PauseWorkerAsync(host);
        var root = assets.AdditionalLibrary("retry");
        var library = await RegisterAsync(host, session, root, "故障恢复验证");
        var first = await TrialHostIntegrationHttp.ControlAsync(host, session, "library_scans.start", Body(library), Guid.NewGuid());
        Assert.AreEqual(200, first.Status);
        var firstTask = first.Payload["body"]!["scan"]!["task_id"]!.GetValue<string>();
        assets.SetOffline(root, true);
        try
        {
            await host.RestartAsync();
            await WaitForAsync(host, session, library, "failed");
        }
        finally
        {
            assets.SetOffline(root, false);
        }

        await host.Services.GetRequiredService<ILibraryAvailability>().RefreshAsync(new LibraryId(library), CancellationToken.None);
        var retry = await TrialHostIntegrationHttp.ControlAsync(host, session, "library_scans.start", Body(library), Guid.NewGuid());
        Assert.AreEqual(200, retry.Status);
        Assert.AreNotEqual(firstTask, retry.Payload["body"]!["scan"]!["task_id"]!.GetValue<string>());
        await WaitForAsync(host, session, library, "succeeded");
    }

    public static async Task AssertQueuedCancellationAsync(TrialHostIntegrationFixture host,
        TrialHostIntegrationAssets assets, TrialHostIntegrationSession session)
    {
        await PauseWorkerAsync(host);
        var library = await RegisterAsync(host, session, assets.AdditionalLibrary("cancel"), "取消验证");
        var queued = await TrialHostIntegrationHttp.ControlAsync(host, session, "library_scans.start", Body(library), Guid.NewGuid());
        Assert.AreEqual(200, queued.Status);
        var body = Body(library);
        body["task_id"] = queued.Payload["body"]!["scan"]!["task_id"]!.GetValue<string>();
        var cancelled = await TrialHostIntegrationHttp.ControlAsync(host, session, "library_scans.cancel", body, Guid.NewGuid());
        Assert.AreEqual(200, cancelled.Status);
        await host.RestartAsync();
        await WaitForAsync(host, session, library, "cancelled");
        var retry = await TrialHostIntegrationHttp.ControlAsync(host, session, "library_scans.start", Body(library), Guid.NewGuid());
        Assert.AreEqual(200, retry.Status);
        await WaitForAsync(host, session, library, "succeeded");
    }

    private static async Task PauseWorkerAsync(TrialHostIntegrationFixture host) =>
        await host.Services.GetServices<IHostedService>().OfType<TrialScanWorker>().Single().StopAsync(CancellationToken.None);

    private static async Task<Guid> RegisterAsync(TrialHostIntegrationFixture host, TrialHostIntegrationSession session,
        string root, string title)
    {
        var registered = await TrialHostIntegrationHttp.ControlAsync(host, session, "libraries.register", new JsonObject
        {
            ["source_key"] = "fixtures",
            ["display_name"] = title,
            ["root_path"] = root,
        }, Guid.NewGuid());
        Assert.AreEqual(200, registered.Status);
        return Guid.Parse(registered.Payload["body"]!["library_id"]!.GetValue<string>());
    }

    private static async Task WaitForAsync(TrialHostIntegrationFixture host, TrialHostIntegrationSession session,
        Guid library, string expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string? state;
        do
        {
            var response = await TrialHostIntegrationHttp.ControlAsync(host, session, "library_scans.get", Body(library));
            Assert.AreEqual(200, response.Status);
            state = response.Payload["body"]!["scan"]!["state"]!.GetValue<string>();
            if (state == expected) return;
            Assert.IsFalse(state is "failed" or "cancelled" or "succeeded", $"Expected {expected}, observed {state}.");
            await Task.Delay(100, deadline.Token);
        } while (!deadline.IsCancellationRequested);
        Assert.Fail($"Expected {expected}, observed {state}.");
    }

    private static JsonObject Body(Guid library, bool browse = false)
    {
        var body = new JsonObject { ["library_id"] = library.ToString("D") };
        if (browse) body["parent_relative_path"] = "";
        return body;
    }
}
