using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Windows.Session;

[SupportedOSPlatform("windows")]
public static class LocalPipe
{
    public static string UserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new IOException("Token user unavailable.");
        }
    }
    public static uint SessionId { get { using var process = Process.GetCurrentProcess(); return (uint)process.SessionId; } }
    public static string UserSession => UserSid + "." + SessionId.ToString(CultureInfo.InvariantCulture);
    public static string ControlEndpoint => "AssetLibrary.HostControl.v1." + UserSession;
    public static string SnapshotEndpoint => "AssetLibrary.Explorer.v1." + UserSession;
    public static bool ControlEndpointExists() => WaitNamedPipe("\\\\.\\pipe\\" + ControlEndpoint, 1)
        || Marshal.GetLastPInvokeError() is not (2 or 3 or 53);

    public static NamedPipeServerStream CreateServer(string name, bool first)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;" + UserSid + ")", 1, out var descriptor, out _))
        { throw new IOException("Pipe security creation failed."); }
        try
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var pipe = CreateNamedPipe("\\\\.\\pipe\\" + name, 0x40000003u | (first ? 0x00080000u : 0),
                0x00000008, 4, 65552, 65552, 0, ref security);
            if (pipe.IsInvalid) { pipe.Dispose(); throw new IOException("Pipe reservation failed."); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, pipe); }
            catch { pipe.Dispose(); throw; }
        }
        finally { _ = LocalFree(descriptor); }
    }

    public static bool IsCurrentClientSession(SafePipeHandle pipe) =>
        GetNamedPipeClientSessionId(pipe, out var session) && session == SessionId;

    public static async Task<FileStream> OpenClientAsync(string name, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            // Identification SQOS prevents a rogue endpoint from impersonating the settings process.
            var handle = CreateFile("\\\\.\\pipe\\" + name, 0xC0000000, 0, IntPtr.Zero, 3, 0x40110000, IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                try
                {
                    VerifyServer(handle);
                    return new FileStream(handle, FileAccess.ReadWrite, 4096, isAsync: true);
                }
                catch { handle.Dispose(); throw; }
            }
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is not (2 or 231)) { throw new IOException("Local Host connection failed."); }
            await Task.Delay(50, token).ConfigureAwait(false);
        }
    }

    private static void VerifyServer(SafeFileHandle pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var processId)
            || !ProcessIdToSessionId(processId, out var session) || session != SessionId)
        { throw new IOException("Host session rejected."); }
        using var process = OpenProcess(0x1000, false, processId);
        if (process.IsInvalid || !OpenProcessToken(process, 0x0008, out var token))
        { throw new IOException("Host identity unavailable."); }
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            if (identity.User?.Value != UserSid) { throw new IOException("Host user rejected."); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr Descriptor;
        internal int InheritHandle;
    }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision, out IntPtr security, out uint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint access, uint mode, uint instances, uint outputSize,
        uint inputSize, uint timeout, ref SecurityAttributes security);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint session);
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitNamedPipe(string name, uint timeout);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(string filename, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafeFileHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
