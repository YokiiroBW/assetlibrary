using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AssetLibrary.ReadCore.Tests;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Preview.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsImageBoundaryTests
{
    [TestInitialize]
    public void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows LPAC boundaries are required.");
    }

    [TestMethod]
    public async Task WorkerCannotReadHostFileOrWriteItsRxDirectory()
    {
        using var sandbox = new RepositorySandbox();
        var hostFile = Path.Combine(sandbox.Root, "host-only.txt");
        File.WriteAllText(hostFile, "synthetic private marker");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        await probe.SendAsync('F', WindowsImageProbe.PathArgument(hostFile), deadline.Token);
        var result = await probe.FinishAsync(deadline.Token);
        Assert.AreEqual("5", result["read_error"]);
        Assert.AreEqual("5", result["write_error"]);
        Assert.AreEqual("synthetic private marker", File.ReadAllText(hostFile));
        Assert.IsFalse(Directory.EnumerateFiles(sandbox.Root, "untrusted-created.txt", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    public async Task InheritableEventOutsideStdioWhitelistIsNotShared()
    {
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset);
        Assert.IsTrue(Native.SetHandleInformation(signal.SafeWaitHandle, 1, 1));
        using var sandbox = new RepositorySandbox();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var baseline = await WindowsImageProbe.RunTrustedControlAsync('B', null, sandbox.Root, deadline.Token);
        Assert.AreEqual("1", baseline["control_created"]);
        Assert.AreEqual("1", baseline["control_signaled"]);
        Assert.AreEqual("1", baseline["control_set"]);
        await using var probe = await WindowsImageProbe.StartAsync(Path.Combine(sandbox.Root, "profiles"), deadline.Token);
        var argument = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(argument, checked((ulong)signal.SafeWaitHandle.DangerousGetHandle()));
        await probe.SendAsync('H', argument, deadline.Token);
        var result = await probe.FinishAsync(deadline.Token);
        Assert.AreEqual("0", result["set"]);
        Assert.IsTrue(result["error"] == "6" || result["exception"] == "C0000008");
        Assert.IsFalse(signal.WaitOne(0));
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetHandleInformation(SafeWaitHandle handle, uint mask, uint flags);
    }
}
