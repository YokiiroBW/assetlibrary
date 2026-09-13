using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.ImageSupervisor;

[SupportedOSPlatform("linux")]
internal sealed class ImageChild : IDisposable
{
    internal const string Executable = "/app/workers/image-preview/AssetLibrary.ImagePreview.Worker";
    private readonly Process process;
    private readonly SafeFileHandle? processHandle;
    internal Stream Input => process.StandardInput.BaseStream;
    internal Stream Output => process.StandardOutput.BaseStream;
    internal Stream Errors => process.StandardError.BaseStream;
    internal int ExitCode => process.ExitCode;
    private ImageChild(Process process, SafeFileHandle? handle) { this.process = process; processHandle = handle; }
    internal static async Task<ImageChild> StartAsync(string mode, CancellationToken token)
    {
        if (mode != "--container-decoder" && !SupervisorModes.IsProbe(mode)) throw new InvalidDataException("Invalid child mode.");
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(Executable)!,
        };
        start.ArgumentList.Add(mode); start.Environment.Clear();
        start.Environment["DOTNET_EnableDiagnostics"] = "0"; start.Environment["LANG"] = "C";
        // A late native startup cannot be abandoned while serving new requests. PID1 exits on this failure.
        var launch = Task.Run(() => Start(start), CancellationToken.None);
        try { return await launch.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try
            {
                using var late = await launch.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
                await late.StopAsync().ConfigureAwait(false);
            }
            catch (TimeoutException) { throw new ImageNamespaceFailure("image_startup_timeout"); }
            catch (Exception failure) when (failure is IOException or System.ComponentModel.Win32Exception) { /* Native creation failed; there is no surviving child. */ }
            throw new OperationCanceledException(token);
        }
    }
    private static ImageChild Start(ProcessStartInfo start)
    {
        var process = Process.Start(start) ?? throw new IOException("Image startup failed.");
        var descriptor = Native.Call(434, process.Id, 0, 0, 0);
        if (descriptor >= 0) return new ImageChild(process, new SafeFileHandle(descriptor, ownsHandle: true));
        if (process.HasExited) return new ImageChild(process, null);
        process.Dispose();
        throw new ImageNamespaceFailure("image_pidfd_unavailable");
    }
    internal async Task ReapAsync(bool terminate, CancellationToken token)
    {
        if (terminate && !process.HasExited)
        {
            if (processHandle is null || Native.Call(424, processHandle.DangerousGetHandle(), 9, 0, 0) != 0)
            {
                if (!process.HasExited) throw new ImageNamespaceFailure("image_termination_failed");
            }
        }
        await process.WaitForExitAsync(token).ConfigureAwait(false);
    }
    internal async Task StopAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await ReapAsync(terminate: true, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw new ImageNamespaceFailure("image_reap_timeout"); }
    }
    public void Dispose() { processHandle?.Dispose(); process.Dispose(); }
    private static class Native
    {
        [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern nint Call(nint number, nint a, nint b, nint c, nint d);
    }
}
internal sealed class ImageNamespaceFailure(string stage) : Exception(stage);
internal static class SupervisorModes
{
    internal static bool IsProbe(string mode) => mode is "--probe-container-isolation" or "--probe-container-memory"
        or "--probe-container-cpu" or "--probe-container-threads";
}
