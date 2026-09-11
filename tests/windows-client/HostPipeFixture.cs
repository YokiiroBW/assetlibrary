using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[SupportedOSPlatform("windows")]
internal sealed class HostPipeFixture : IAsyncDisposable
{
    private readonly ClientTransport transport;
    internal SnapshotStore Store { get; }
    internal SnapshotPipeServer Server { get; }

    private HostPipeFixture()
    {
        var handler = new ProtocolFixture((request, _) => ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries()));
        transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(3));
        Store = new SnapshotStore(new ReadOnlyClient(transport), DateTimeOffset.UtcNow.AddHours(1));
        Server = new SnapshotPipeServer(Store, NativePipe.EndpointName + ".test." + Guid.NewGuid().ToString("N"));
    }

    internal static async Task<HostPipeFixture> CreateAsync(bool prime = false)
    {
        var fixture = new HostPipeFixture();
        if (prime) { _ = await HostTestSupport.SettledAsync(fixture.Store); }
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        try { await Server.DisposeAsync(); await Store.DisposeAsync(); }
        finally { transport.Dispose(); }
    }
}
