using System.Runtime.Versioning;
using AssetLibrary.Windows.Client;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
public sealed class ThumbnailPipeServer : IAsyncDisposable
{
    private readonly ImagePipeServer server;
    public static string Endpoint => "AssetLibrary.ExplorerThumbnail.v1." + LocalPipe.UserSession;
    public ThumbnailPipeServer(Func<ThumbnailRequest, CancellationToken, Task<ThumbnailResponse>> read, string? endpoint = null)
        : this(read, new ImageClientCapacity(), endpoint) { }
    internal ThumbnailPipeServer(Func<ThumbnailRequest, CancellationToken, Task<ThumbnailResponse>> read, ImageClientCapacity capacity, string? endpoint = null)
    { server = new ImagePipeServer(read, DerivedImageProfile.Thumbnail512, capacity, endpoint ?? Endpoint); }
    public Task Completion => server.Completion;
    public ValueTask DisposeAsync() => server.DisposeAsync();
}
