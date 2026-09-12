using System.Net;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[SupportedOSPlatform("windows")]
internal static class SessionTestSupport
{
    internal static ControlRequest Connect(string account = "fixture") => new(1, Guid.NewGuid(), ControlOperation.Connect,
        new ConnectionInput("https://fixture.example", null, account, "synthetic-secret", false));
    internal static string TemporaryPath() => Path.Combine(Path.GetTempPath(), "AssetLibrarySessionTests-" + Guid.NewGuid().ToString("N"));
    internal static ClientTransport Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(ProtocolFixture.Profile, new ProtocolFixture(respond), TimeSpan.FromSeconds(3));
    internal static HttpResponseMessage Login(DateTimeOffset? expiry = null) => ProtocolFixture.Json(new JsonObject
    {
        ["authenticated"] = true,
        ["principal_id"] = "synthetic-principal",
        ["display_name"] = "合成账号",
        ["csrf_token"] = new string('x', 43),
        ["absolute_expires_at"] = (expiry ?? DateTimeOffset.UtcNow.AddHours(1)).ToString("O"),
    });
    internal static async Task<SnapshotResponse> ReadyAsync(UserSessionCoordinator coordinator)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            var page = coordinator.Query(HostTestSupport.Root);
            if (page.Status == SnapshotStatus.Ready) { return page; }
            await Task.Delay(10, deadline.Token);
        }
    }
}
