using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.ReadCore.Tests;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Preview.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsImageParentExitTests
{
    [TestMethod]
    public async Task ParentCrashKillsWorkerEvenWhileControllerKeepsStdinOpen()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows parent-exit Job boundary is required."); return; }
        var launcher = Environment.GetEnvironmentVariable("ASSETLIBRARY_WINDOWS_IMAGE_PARENT_DLL");
        var dotnet = Environment.GetEnvironmentVariable("ASSETLIBRARY_WINDOWS_IMAGE_DOTNET");
        if (string.IsNullOrWhiteSpace(launcher) || string.IsNullOrWhiteSpace(dotnet))
        {
            Assert.Inconclusive("The pinned runtime and owned crash-parent tool are required."); return;
        }
        using var sandbox = new RepositorySandbox();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var state = Path.Combine(sandbox.Root, "profiles");
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(launcher);
        start.ArgumentList.Add(typeof(WindowsImageProcess).Assembly.Location);
        start.ArgumentList.Add(WindowsImageProbe.Executable());
        start.ArgumentList.Add(state);
        using var parent = Process.Start(start)!;
        Process? child = null;
        SafeFileHandle? retainedInput = null;
        try
        {
            var line = await parent.StandardOutput.ReadLineAsync(deadline.Token);
            Assert.IsNotNull(line);
            var values = line.Split(';').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);
            child = Process.GetProcessById(int.Parse(values["child"], CultureInfo.InvariantCulture));
            _ = child.SafeHandle;
            var input = new nint(long.Parse(values["stdin"], CultureInfo.InvariantCulture));
            Assert.IsTrue(Native.DuplicateHandle(parent.SafeHandle, input, new nint(-1), out retainedInput, 0, false, 2));
            Assert.IsFalse(retainedInput.IsInvalid);
            Assert.IsFalse(child.HasExited);
            parent.Kill(); // Only the parent, never a process-tree kill that could fake the Job result.
            await parent.WaitForExitAsync(deadline.Token);
            await child.WaitForExitAsync(deadline.Token);
            Assert.IsFalse(retainedInput.IsClosed);
            Assert.AreEqual(1, WindowsImageProfile.Recover(state));
            Assert.AreEqual(0, Directory.EnumerateFiles(state, "*.owner*", SearchOption.AllDirectories).Count());
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(2000); }
            retainedInput?.Dispose();
            child?.Dispose();
        }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DuplicateHandle(SafeProcessHandle source, nint handle, nint target,
            out SafeFileHandle duplicate, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    }
}
