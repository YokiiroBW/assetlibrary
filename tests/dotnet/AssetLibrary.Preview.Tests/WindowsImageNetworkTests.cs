using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using AssetLibrary.ReadCore.Tests;

namespace AssetLibrary.Preview.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsImageNetworkTests
{
    [TestInitialize]
    public void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Actual Windows network boundary is required.");
    }

    [TestMethod]
    public async Task LpacWinsockInitializationBlocksThisTestedPath()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using (var control = new TcpClient())
        {
            await control.ConnectAsync(IPAddress.Loopback, port);
            using var accepted = await listener.AcceptTcpClientAsync();
            Assert.IsTrue(control.Connected);
        }
        using var sandbox = new RepositorySandbox();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var arguments = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(arguments, port);
        IPAddress.Loopback.GetAddressBytes().CopyTo(arguments, 4);
        var baseline = await WindowsImageProbe.RunTrustedControlAsync('N', arguments, sandbox.Root, deadline.Token);
        Assert.AreEqual("0", baseline["socket_error"]);
        Assert.AreEqual("0", baseline["connect_error"]);
        using (var accepted = await listener.AcceptTcpClientAsync(deadline.Token)) { }
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        await probe.SendAsync('N', arguments, deadline.Token);
        var result = await probe.FinishAsync(deadline.Token);
        // This is an initialization boundary, not a claim of direct WFP connection rejection.
        Assert.AreEqual("10107", result["socket_error"]);
        Assert.AreEqual("0", result["connect_error"]);
        Assert.IsFalse(listener.Pending());
    }

    [TestMethod]
    public async Task LpacWinHttpInitializationBlocksThisTestedPath()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var sandbox = new RepositorySandbox();
        var arguments = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(arguments, ((IPEndPoint)listener.LocalEndpoint).Port);
        IPAddress.Loopback.GetAddressBytes().CopyTo(arguments, 4);
        var serving = ServeControlAsync(listener, deadline.Token);
        var control = await WindowsImageProbe.RunTrustedControlAsync('W', arguments, sandbox.Root, deadline.Token);
        await serving;
        Assert.AreEqual("1", control["http_sent"]);
        Assert.AreEqual("0", control["http_error"]);
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        await probe.SendAsync('W', arguments, deadline.Token);
        var result = await probe.FinishAsync(deadline.Token);
        Assert.AreEqual("0", result["http_sent"]);
        Assert.IsFalse(listener.Pending(), $"http_error={result["http_error"]}");
        Assert.AreEqual("12004", result["http_error"]);
    }

    private static async Task ServeControlAsync(TcpListener listener, CancellationToken token)
    {
        using var connection = await listener.AcceptTcpClientAsync(token);
        var stream = connection.GetStream();
        var buffer = new byte[1024];
        _ = await stream.ReadAsync(buffer, token);
        await stream.WriteAsync("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), token);
    }

}
