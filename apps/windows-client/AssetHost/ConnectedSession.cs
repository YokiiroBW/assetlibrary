using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

internal sealed class ConnectedSession(ClientTransport transport, ClientSession session) : IAsyncDisposable
{
    private readonly SnapshotStore snapshots = new(new ReadOnlyClient(transport), session.ExpiresAt);
    internal bool IsRevoked => snapshots.IsRevoked;
    internal SnapshotResponse Query(SnapshotRequest request) => snapshots.Query(request);
    internal void Prime() => snapshots.Query(new SnapshotRequest(1, Guid.Empty, Guid.Empty));

    public async ValueTask DisposeAsync()
    {
        try
        {
            await snapshots.DisposeAsync().ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await transport.SignOutAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is ClientException or OperationCanceledException)
        { Console.WriteLine("{\"operation\":\"logout\",\"status\":\"server_unconfirmed_local_clear\"}"); }
        finally { transport.Dispose(); }
    }
}
