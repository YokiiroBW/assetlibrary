using System.Runtime.Versioning;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal static class UserSessionHost
{
    internal static async Task<int> RunAsync()
    {
        await using var notifier = new ShellSessionNotifier();
        await using var session = new UserSessionCoordinator(new UserConnectionStore(), changed: notifier.Changed);
        // Reserve the production endpoint before loading credentials. A second launch never retries login.
        await using var control = new ControlPipeServer(session);
        await using var snapshots = new SnapshotPipeServer(session.Query, LocalPipe.SnapshotEndpoint);
        using var stopping = new CancellationTokenSource();
        var initialization = session.InitializeAsync(stopping.Token);
        try { await Task.WhenAny(control.Completion, snapshots.Completion).Unwrap().ConfigureAwait(false); }
        finally { await stopping.CancelAsync().ConfigureAwait(false); await initialization.ConfigureAwait(false); }
        return 0;
    }

    internal static async Task<int> ShutdownAsync()
    {
        if (!LocalPipe.ControlEndpointExists()) { return 0; }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var response = await ControlClient.SendAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Shutdown), deadline.Token).ConfigureAwait(false);
        return response.Ok ? 0 : 3;
    }
}
