using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using AssetLibrary.ReadCore.Tests;

namespace AssetLibrary.Preview.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsImageResourceTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestInitialize]
    public void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Actual Windows Job limits are required.");
    }

    [TestMethod]
    public async Task KernelCpuQuotaTerminatesWithMeasuredPeriodicOvershoot()
    {
        using var sandbox = new RepositorySandbox();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        _ = probe.Child.Process.SafeHandle;
        var timer = Stopwatch.StartNew();
        await probe.SendAsync('C', null, deadline.Token);
        var result = await probe.FinishAsync(deadline.Token);
        Assert.AreEqual("30000000", result["cpu_ticks"]);
        Assert.IsFalse(deadline.IsCancellationRequested);
        Assert.IsGreaterThanOrEqualTo(2.5, probe.Child.Process.UserProcessorTime.TotalSeconds);
        TestContext.WriteLine($"ConfiguredCPUSeconds=3;UserCPUSeconds={probe.Child.Process.UserProcessorTime.TotalSeconds};WallSeconds={timer.Elapsed.TotalSeconds};Exit={probe.Child.ExitCode:X8}");
        Assert.AreEqual(unchecked((int)0xC0000044), probe.Child.ExitCode);
    }

    [TestMethod]
    public async Task CommittedMemoryIsBoundedByJobRatherThanGc()
    {
        using var sandbox = new RepositorySandbox();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        await probe.SendAsync('M', null, deadline.Token);
        var result = await probe.FinishAsync(deadline.Token);
        var blocks = int.Parse(result["blocks"], CultureInfo.InvariantCulture);
        Assert.IsTrue(blocks is >= 32 and < 64);
        Assert.AreEqual("1", result["measured"]);
        Assert.AreEqual("536870912", result["job_memory"]);
        Assert.IsLessThanOrEqualTo(536870912L, long.Parse(result["private_bytes"], CultureInfo.InvariantCulture));
        Assert.AreNotEqual("0", result["error"]);
        Assert.AreEqual(0, probe.Child.ExitCode);
        Assert.IsFalse(deadline.IsCancellationRequested);
    }

    [TestMethod]
    public async Task JobRejectsAnotherProcess()
    {
        using var sandbox = new RepositorySandbox();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        await probe.SendAsync('P', null, deadline.Token);
        var result = await probe.FinishAsync(deadline.Token);
        Assert.AreEqual("0", result["created"]);
        Assert.AreNotEqual("0", result["error"]);
    }

    [TestMethod]
    public async Task CancellationTerminatesSleepingWorkerAndReaps()
    {
        using var sandbox = new RepositorySandbox();
        using var cancellation = new CancellationTokenSource();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), cancellation.Token);
        await probe.SendAsync('S', null, guard.Token);
        var timer = Stopwatch.StartNew();
        cancellation.Cancel();
        _ = await probe.FinishAsync(guard.Token);
        Assert.IsLessThan(2.0, timer.Elapsed.TotalSeconds);
        Assert.AreNotEqual(0, probe.Child.ExitCode);
    }
}
