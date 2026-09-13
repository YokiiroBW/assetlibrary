using System.Buffers.Binary;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[SupportedOSPlatform("windows")]
internal static class ThumbnailLiveSupport
{
    internal static async Task<SnapshotResponse> ImageFixturePageAsync(SnapshotStore store)
    {
        var libraries = await HostTestSupport.SettledAsync(store);
        var page = await HostTestSupport.SettledAsync(store, HostTestSupport.Open(libraries, libraries.Items.Single()));
        for (var index = 0; index < 3; ++index)
        {
            var directory = page.Items.SingleOrDefault(item => item.Kind == SnapshotKind.Directory && item.Name == "图片样例");
            if (directory is not null) { return await HostTestSupport.SettledAsync(store, HostTestSupport.Open(page, directory)); }
            var next = page.Items.Single(item => item.Kind == SnapshotKind.NextPage);
            page = await HostTestSupport.SettledAsync(store, HostTestSupport.Open(page, next));
        }
        throw new InvalidOperationException("Explicit synthetic image fixture directory is required.");
    }

    internal static async Task<ThumbnailResponse> ReadPipeAsync(string endpoint, ThumbnailRequest request, CancellationToken token,
        DerivedImageProfile profile = DerivedImageProfile.Thumbnail512)
    {
        await using var pipe = await LocalPipe.OpenClientAsync(endpoint, token);
        await pipe.WriteAsync(ImageProjectionProtocol.EncodeRequest(profile, request), token);
        var header = new byte[16]; await pipe.ReadExactlyAsync(header, token);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        Assert.IsLessThanOrEqualTo((uint)(56 + DerivedImageSpecification.For(profile).MaximumDecodedBytes), size);
        var frame = new byte[16 + size]; header.CopyTo(frame, 0);
        await pipe.ReadExactlyAsync(frame.AsMemory(16), token);
        return ImageProjectionProtocol.DecodeResponse(profile, frame, request);
    }
}
