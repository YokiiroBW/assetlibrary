using System.Runtime.Versioning;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.ReadCore.Tests;

namespace AssetLibrary.Preview.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsImageIdentityTests
{
    [TestMethod]
    public async Task ChangedPositiveTimestampDoesNotHideLiveOwnedWorker()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Actual Windows worker identity is required."); return; }
        using var sandbox = new RepositorySandbox();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var profile = WindowsImageProfile.Create(WindowsImageProbe.Executable(), Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        await using var worker = WindowsImageProcess.Start(profile, deadline.Token);
        await worker.Input.WriteAsync(new byte[] { (byte)'S' }, deadline.Token);
        await worker.Input.FlushAsync(deadline.Token);
        using var output = new StreamReader(worker.Output, leaveOpen: true);
        Assert.AreEqual("ready=1", await output.ReadLineAsync(deadline.Token));
        Assert.IsFalse(WindowsImageProcessIdentity.HasExited(checked((uint)worker.Process.Id), 1, profile.Sid));
        Assert.IsFalse(worker.Process.HasExited);
    }
}
