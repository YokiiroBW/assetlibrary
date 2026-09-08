using System.Runtime.Versioning;
using AssetLibrary.ReadCore.Tests;

namespace AssetLibrary.Preview.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsComBoundaryTests
{
    [TestMethod]
    public async Task OutOfProcessTransferBrokerCannotCreateAJob()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Actual Windows COM isolation is required."); return; }
        using var sandbox = new RepositorySandbox();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var control = await WindowsImageProbe.RunTrustedControlAsync('T', null, sandbox.Root, deadline.Token);
        Assert.AreEqual("00000000", control["bits_create"]);
        Assert.AreEqual("00000000", control["bits_job"]);
        Assert.AreEqual("00000000", control["bits_cancel"]);
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        await probe.SendAsync('T', null, deadline.Token);
        var result = await probe.FinishAsync(deadline.Token);
        Assert.IsTrue(result.GetValueOrDefault("bits_create") == "80070005" || result.GetValueOrDefault("bits_job") == "80070005",
            $"BITS result: {string.Join(';', result.Select(field => field.Key + '=' + field.Value))}");
    }

    [TestMethod]
    public async Task InProcessComCannotReadHostOnlyMarker()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Actual Windows COM isolation is required."); return; }
        using var sandbox = new RepositorySandbox();
        var marker = Path.Combine(sandbox.Root, "com-host-only.txt");
        File.WriteAllText(marker, "synthetic COM marker");
        var argument = WindowsImageProbe.PathArgument(marker);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var control = await WindowsImageProbe.RunTrustedControlAsync('O', argument, sandbox.Root, deadline.Token);
        Assert.AreEqual("00000000", control["com_create"]);
        Assert.AreEqual("00000000", control["com_open"]);
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        await probe.SendAsync('O', argument, deadline.Token);
        var result = await probe.FinishAsync(deadline.Token);
        Assert.IsTrue(result.GetValueOrDefault("com_create") == "80070005"
            || result.GetValueOrDefault("com_exception") == "800A0046",
            $"COM result: {string.Join(';', result.Select(field => field.Key + '=' + field.Value))}");
        Assert.AreEqual("synthetic COM marker", File.ReadAllText(marker));
    }
}
