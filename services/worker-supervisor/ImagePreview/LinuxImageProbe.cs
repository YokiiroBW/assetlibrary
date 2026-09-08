using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetLibrary.ImagePreview.Worker;

internal sealed class LinuxImageProbe : IDisposable
{
    private readonly string mode;
    private readonly string marker;
    private readonly int initialSeccomp;
    private readonly int initialIoUringError;
    private readonly int socket;

    [SupportedOSPlatform("linux")]
    private LinuxImageProbe(string mode)
    {
        this.mode = mode;
        marker = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "preview-denied.marker"));
        if (mode == "--probe-isolation")
        {
            using var file = File.OpenRead(marker);
            Span<byte> expected = stackalloc byte[65];
            var count = file.ReadAtLeast(expected, expected.Length, throwOnEndOfStream: false);
            if (!expected[..count].SequenceEqual("assetlibrary-image-canary-v1"u8))
            {
                throw new InvalidDataException("The owned probe canary is missing or invalid.");
            }
        }
        initialSeccomp = LinuxImageIsolation.CurrentMode();
        initialIoUringError = Error(425, 1, 0); // Invalid params are harmless; distinguish outer-policy EPERM.
        socket = checked((int)Native.Call(41, 2, 1, 0, 0, 0, 0));
        if (socket < 0) throw new InvalidDataException("The probe's pre-filter socket control failed.");
        _ = JsonSerializer.SerializeToUtf8Bytes(new ImageProbeReport("warmup", false), ImageProbeJsonContext.Default.ImageProbeReport);
    }

    [SupportedOSPlatform("linux")]
    public static LinuxImageProbe Prepare(string mode) => new(mode);

    [SupportedOSPlatform("linux")]
    public int Run(Stream output)
    {
        if (mode == "--probe-cpu")
        {
            while (true) Thread.SpinWait(10000);
        }
        var report = mode == "--probe-memory" ? Memory()
            : mode == "--probe-threads" ? Threads() : Isolation();
        output.Write(JsonSerializer.SerializeToUtf8Bytes(report, ImageProbeJsonContext.Default.ImageProbeReport));
        output.Flush();
        return report.Passed ? 0 : 1;
    }

    [SupportedOSPlatform("linux")]
    private ImageProbeReport Isolation()
    {
        var fileDenied = false;
        try
        {
            using var unexpected = File.OpenRead(marker);
        }
        catch (UnauthorizedAccessException)
        {
            fileDenied = true;
        }
        var socketDenied = Error(41, 2, 1) == 1;
        var address = GCHandle.Alloc(new byte[] { 2, 0, 0, 0, 127, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 }, GCHandleType.Pinned);
        bool connectDenied;
        try { connectDenied = Error(42, socket, address.AddrOfPinnedObject(), 16) == 1; }
        finally { address.Free(); }
        var otherProcessDenied = Error(62, int.MaxValue, 0) == 1;
        var memoryReadDenied = Error(310, int.MaxValue, 0, 0) == 1;
        var forkDenied = DeniedFork(57, 0);
        var cloneDenied = DeniedFork(56, 17);
        var unavailableName = GCHandle.Alloc(Encoding.UTF8.GetBytes("/assetlibrary-probe-nonexistent-executable\0"), GCHandleType.Pinned);
        bool execDenied;
        try { execDenied = Error(59, unavailableName.AddrOfPinnedObject(), 0, 0) == 1; }
        finally { unavailableName.Free(); }
        var ringDenied = Error(425, 1, 0) == 1;
        var active = LinuxImageIsolation.CurrentMode();
        var noPrivilege = LinuxImageIsolation.NoNewPrivileges();
        var passed = fileDenied && socketDenied && connectDenied && otherProcessDenied && memoryReadDenied
            && forkDenied && cloneDenied && execDenied && ringDenied && active == 2 && noPrivilege == 1;
        return new ImageProbeReport("isolation", passed, initialSeccomp, active, noPrivilege, fileDenied,
            socketDenied, connectDenied, otherProcessDenied, memoryReadDenied, forkDenied, cloneDenied, execDenied,
            initialIoUringError, ringDenied);
    }

    private static ImageProbeReport Memory()
    {
        nint allocation = nint.Zero;
        var denied = false;
        try { allocation = Marshal.AllocHGlobal(768 * 1024 * 1024); }
        catch (OutOfMemoryException) { denied = true; }
        finally { if (allocation != nint.Zero) Marshal.FreeHGlobal(allocation); }
        return new ImageProbeReport("memory", denied);
    }

    private static ImageProbeReport Threads()
    {
        var gate = new ThreadProbeGate();
        List<Thread> started = [];
        var denied = false;
        var joined = true;
        try
        {
            for (var index = 0; index < 257; index++)
            {
                var thread = new Thread(() =>
                {
                    while (Volatile.Read(ref gate.Stop) == 0) Thread.Sleep(10);
                }, 64 * 1024)
                { IsBackground = true };
                thread.Start();
                started.Add(thread);
            }
        }
        catch (Exception failure) when (failure is OutOfMemoryException or ThreadStartException)
        {
            denied = true;
        }
        finally
        {
            Volatile.Write(ref gate.Stop, 1);
            var stamp = Stopwatch.GetTimestamp();
            foreach (var thread in started)
            {
                var remaining = Math.Max(0, 1000 - (int)Stopwatch.GetElapsedTime(stamp).TotalMilliseconds);
                joined &= thread.Join(remaining);
            }
        }
        return new ImageProbeReport("threads", denied && joined && started.Count < 256, CreatedThreads: started.Count);
    }

    private static bool DeniedFork(nint number, nint flags)
    {
        var child = Native.Call(number, flags, 0, 0, 0, 0, 0);
        var error = Marshal.GetLastPInvokeError();
        if (child == 0) Native.ExitImmediately(99);
        if (child > 0) _ = Native.Call(61, child, 0, 0, 0, 0, 0);
        return child == -1 && error == 1;
    }

    private static int Error(nint number, nint first = 0, nint second = 0, nint third = 0)
    {
        var result = Native.Call(number, first, second, third, 0, 0, 0);
        var error = Marshal.GetLastPInvokeError();
        if (number == 41 && result >= 0) _ = Native.Call(3, result, 0, 0, 0, 0, 0);
        return result == -1 ? error : 0;
    }

    public void Dispose()
    {
        _ = Native.Call(3, socket, 0, 0, 0, 0, 0);
        GC.SuppressFinalize(this);
    }

    private sealed class ThreadProbeGate { public int Stop; }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "syscall", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern nint Call(nint number, nint a, nint b, nint c, nint d, nint e, nint f);

        [DoesNotReturn]
        [DllImport("libc", EntryPoint = "_exit", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern void ExitImmediately(int status);
    }
}

internal sealed record ImageProbeReport(string Probe, bool Passed, int InitialSeccomp = 0, int Seccomp = 0,
    int NoNewPrivileges = 0, bool FileDenied = false, bool SocketDenied = false, bool ConnectDenied = false,
    bool OtherProcessSignalDenied = false, bool CrossProcessMemoryDenied = false, bool ForkDenied = false,
    bool CloneDenied = false, bool ExecDenied = false, int InitialIoUringError = 0, bool IoUringDenied = false,
    int CreatedThreads = 0);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ImageProbeReport))]
internal sealed partial class ImageProbeJsonContext : JsonSerializerContext;
