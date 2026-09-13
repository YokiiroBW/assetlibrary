using System.Net.Sockets;
using System.Runtime.InteropServices;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Contracts;
using AssetLibrary.Modules.PreviewProvider.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class SocketImageConfigurationTests
{
    [TestMethod]
    [DataRow(null, null)]
    [DataRow(null, "/tmp/decoder.sock")]
    [DataRow(null, "unix:///run/assetlibrary-image/decoder.sock")]
    [DataRow(null, " ")]
    [DataRow(" ", "/run/assetlibrary-image/decoder.sock")]
    [DataRow("configured-worker", "/run/assetlibrary-image/decoder.sock")]
    public async Task MissingInvalidAndConflictingConfigurationStaysUnavailable(string? worker, string? socket)
    {
        var scenario = new ImageServiceScenario();
        var query = ImagePreviewRuntime.Create(scenario, new ReadOnlyWorkerProcessOptions("unused", []),
            worker, Path.GetTempPath(), NullLoggerFactory.Instance, socket);
        var failure = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await query.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.Unavailable, failure.Failure);
        Assert.AreEqual(0, scenario.Opened);
    }

    [TestMethod]
    public async Task UnsupportedPlatformCannotActivateProductionSocketBackend()
    {
        if (OperatingSystem.IsLinux()) { Assert.Inconclusive("This negative control applies outside Linux."); return; }
        await MissingInvalidAndConflictingConfigurationStaysUnavailable(null, UnixSocketImageDecoder.SocketPath);
    }

    [TestMethod]
    public async Task RealLinuxPeerUsesEffectiveUserAndRejectsUnconnectedSocket()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("SO_PEERCRED requires actual Linux; protocol tests use an explicit fake peer."); return; }
        using var unconnected = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        Assert.IsFalse(UnixSocketImagePeer.IsRoot(unconnected));
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            Assert.AreEqual(0, await socket.ReceiveAsync(new byte[1], SocketFlags.None, token));
        });
        using var connected = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await connected.ConnectAsync(new UnixDomainSocketEndPoint(server.PathName));
        Assert.AreEqual(Native.GetEffectiveUserId() == 0, UnixSocketImagePeer.IsRoot(connected));
        connected.Dispose(); await server.CompleteAsync();
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "geteuid", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern uint GetEffectiveUserId();
    }
}
