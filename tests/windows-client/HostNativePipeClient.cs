using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Windows.Tests;

[SupportedOSPlatform("windows")]
internal static class HostNativePipeClient
{
    internal static async Task<FileStream> OpenAsync(string name, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            // Match the native Shell's local CreateFileW, overlapped I/O and Identification-only SQOS.
            var handle = CreateFile("\\\\.\\pipe\\" + name, 0xC0000000, 0, IntPtr.Zero, 3, 0x40110000, IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                try { return new FileStream(handle, FileAccess.ReadWrite, 4096, isAsync: true); }
                catch { handle.Dispose(); throw; }
            }
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error != 231) { throw new IOException("Native pipe open failed."); }
            await Task.Delay(5, token);
        }
    }

    internal static async Task AssertDisconnectedAsync(Stream stream, CancellationToken token)
    {
        try { Assert.AreEqual(0, await stream.ReadAsync(new byte[1], token)); }
        catch (IOException closed) when ((closed.HResult & 0xFFFF) is 109 or 233)
        {
            // Unlike NamedPipeClientStream, a raw file handle surfaces native broken-pipe/no-data errors.
            return;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(string filename, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
}
