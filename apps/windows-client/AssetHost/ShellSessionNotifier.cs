using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal sealed class ShellSessionNotifier : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim pending = new(0, 1);
    private readonly Task worker;
    internal ShellSessionNotifier() { worker = RunAsync(); }
    internal void Changed()
    {
        try { pending.Release(); }
        catch (SemaphoreFullException) { /* Coalesce transitions while the fixed root notification is pending. */ }
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await pending.WaitAsync(stopping.Token).ConfigureAwait(false);
                try
                {
                    var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "AssetLibrary.Host.exe"))
                    { UseShellExecute = false, CreateNoWindow = true };
                    info.ArgumentList.Add("--notify-session-changed");
                    using var process = Process.Start(info) ?? throw new IOException("Notification helper unavailable.");
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(3));
                    try
                    {
                        await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                        if (process.ExitCode != 0) { Log("helper_exit", process.ExitCode); }
                    }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited) { process.Kill(entireProcessTree: true); }
                        await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                }
                catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException)
                { Console.WriteLine("{\"operation\":\"session_notify\",\"status\":\"unavailable\"}"); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { /* Owned helper shutdown. */ }
    }

    internal static int Notify(bool probeOnly = false)
    {
        var result = 2;
        var worker = new Thread(() => result = NotifyOnSta(probeOnly)) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (worker.Join(TimeSpan.FromSeconds(2))) { return result; }
        Log("apartment_timeout", unchecked((int)0x800705B4));
        return 2;
    }

    private static int NotifyOnSta(bool probeOnly)
    {
        var initialized = CoInitializeEx(IntPtr.Zero, 2);
        Log("initialize_sta", initialized);
        if (initialized < 0) { return 2; }
        try
        {
            if (probeOnly) { return 0; }
            var result = SHParseDisplayName("::{BBC992DE-CE5D-48C8-A86C-7230C7D72B02}", IntPtr.Zero, out var item, 0, out _);
            Log("parse_root", result);
            if (result < 0) { return 2; }
            try { SHChangeNotify(0x00001000, 0x00002000, item, IntPtr.Zero); }
            finally { Marshal.FreeCoTaskMem(item); }
            Log("dispatched", 0);
            return 0;
        }
        finally { CoUninitialize(); }
    }

    private static void Log(string stage, int hresult) =>
        Console.WriteLine(JsonSerializer.Serialize(new { operation = "session_notify", stage, hresult }));

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        await worker.ConfigureAwait(false);
        pending.Dispose();
        stopping.Dispose();
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint attributes, out uint found);
    [DllImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CoUninitialize();
}
