using System.Buffers.Binary;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[SupportedOSPlatform("windows")]
internal sealed class ThumbnailLiveFixture : IAsyncDisposable
{
    private readonly ClientTransport transport;
    private readonly SnapshotStore store;
    private readonly ThumbnailSession thumbnails;
    private readonly ThumbnailPipeServer server;
    internal string Endpoint { get; } = ThumbnailPipeServer.Endpoint + ".test." + Guid.NewGuid().ToString("N");
    internal SnapshotResponse Page { get; private set; } = null!;
    private ThumbnailLiveFixture(ClientTransport transport, DateTimeOffset expiry)
    {
        this.transport = transport;
        store = new SnapshotStore(new ReadOnlyClient(transport), expiry);
        thumbnails = new ThumbnailSession(transport, store, new ThumbnailDecoder(ThumbnailTestSupport.Executable).DecodeAsync);
        server = new ThumbnailPipeServer(thumbnails.ReadAsync, Endpoint);
    }
    internal static async Task<ThumbnailLiveFixture> OpenAsync(CancellationToken token)
    {
        var profile = await PrivateProfile.LoadAsync(HostTestSupport.RequiredNativeProfilePath(), token);
        var transport = new ClientTransport(profile.Server);
        ThumbnailLiveFixture? fixture = null;
        try
        {
            var session = await transport.SignInAsync(profile.Account, profile.Password, token);
            fixture = new ThumbnailLiveFixture(transport, session.ExpiresAt);
            fixture.Page = await ThumbnailLiveSupport.ImageFixturePageAsync(fixture.store);
            return fixture;
        }
        catch
        {
            if (fixture is not null) { await fixture.DisposeAsync(); }
            else { transport.Dispose(); }
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            await server.DisposeAsync(); await thumbnails.DisposeAsync(); await store.DisposeAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await transport.SignOutAsync(deadline.Token);
        }
        finally { transport.Dispose(); }
    }
}
