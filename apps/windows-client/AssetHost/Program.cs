using System.Globalization;
using System.Text.Json;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Log("startup", "unsupported_platform", 0); return 2; }
        try
        {
            if (args is ["--decode-thumbnail"]) { return await ThumbnailDecodeHelper.RunAsync().ConfigureAwait(false); }
            if (args is ["--user-session"]) { return await UserSessionHost.RunAsync().ConfigureAwait(false); }
            if (args is ["--shutdown-user-session"]) { return await UserSessionHost.ShutdownAsync().ConfigureAwait(false); }
            if (args is ["--notify-session-changed"]) { return ShellSessionNotifier.Notify(); }
            var options = ParseArguments(args);
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(options.LifetimeSeconds));
            using var profileDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            profileDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            var profile = await PrivateProfile.LoadAsync(options.ProfilePath, profileDeadline.Token).ConfigureAwait(false);
            using var transport = new ClientTransport(profile.Server);
            ClientSession session;
            try { session = await transport.SignInAsync(profile.Account, profile.Password, lifetime.Token).ConfigureAwait(false); }
            catch (ClientException failure) { Log("login", failure.ClearsData ? "access_denied" : "unavailable", 0); return 3; }
            // Credentials are never persisted or retried. The transport retains only its memory session.
            profile = null;
            try
            {
                await using var store = new SnapshotStore(new ReadOnlyClient(transport), session.ExpiresAt, log: LogStatus);
                await using var server = new SnapshotPipeServer(store, LogStatus);
                _ = store.Query(new SnapshotRequest(1, Guid.Empty, Guid.Empty));
                Log("host", "ready", 0);
                var stop = WaitForStopAsync(options.StopPath, lifetime.Token);
                try
                {
                    var completed = await Task.WhenAny(stop, server.Completion).ConfigureAwait(false);
                    await completed.ConfigureAwait(false);
                }
                finally
                {
                    await lifetime.CancelAsync().ConfigureAwait(false);
                    await stop.ConfigureAwait(false);
                }
            }
            finally
            {
                using var logout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await transport.SignOutAsync(logout.Token).ConfigureAwait(false); Log("logout", "server_revoked", 0); }
                catch (Exception failure) when (failure is ClientException or OperationCanceledException)
                { Log("logout", "server_unconfirmed", 0); }
                transport.Dispose();
                Log("session", "local_clear", 0);
            }
            return 0;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or JsonException or ArgumentException or ClientException
            or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
        { Log("host", "failed", 0); return 2; }
    }

    private static async Task WaitForStopAsync(string? stopPath, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (stopPath is not null && File.Exists(stopPath)) { return; }
                await Task.Delay(200, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Normal bounded lifetime end. */ }
    }

    private static HostOptions ParseArguments(string[] args)
    {
        string? profile = null;
        string? stop = null;
        var lifetime = 600;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !seen.Add(args[index])) { throw new ArgumentException("Invalid arguments."); }
            switch (args[index])
            {
                case "--profile": profile = Path.GetFullPath(args[index + 1]); break;
                case "--stop-file": stop = Path.GetFullPath(args[index + 1]); break;
                case "--lifetime-seconds":
                    if (!int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out lifetime)
                        || lifetime is < 1 or > 3600) { throw new ArgumentException("Invalid lifetime."); }
                    break;
                default: throw new ArgumentException("Invalid arguments.");
            }
        }
        return new HostOptions(profile ?? throw new ArgumentException("Profile path required."), stop, lifetime);
    }

    private static void LogStatus(string operation, SnapshotStatus status, long elapsed) => Log(operation, status.ToString(), elapsed);
    private static void Log(string operation, string status, long elapsed) =>
        Console.WriteLine(JsonSerializer.Serialize(new { operation, status, elapsed_ms = elapsed }));
    private sealed record HostOptions(string ProfilePath, string? StopPath, int LifetimeSeconds);
}
