using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using AssetLibrary.Windows.AssetHost;

namespace AssetLibrary.Windows.Tests;

[SupportedOSPlatform("windows")]
internal static class HostPipeTestSupport
{
    [SupportedOSPlatform("windows")]
    internal static NamedPipeClientStream NewClient(string name) => new(".", name, PipeDirection.InOut,
        PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);

    internal static async Task<SnapshotResponse> ReadResponseAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[16];
        await stream.ReadExactlyAsync(header, token);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        Assert.IsLessThanOrEqualTo(65536u, size);
        var frame = new byte[16 + size];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(16), token);
        return SnapshotProtocol.DecodeResponse(frame, 7);
    }

}
