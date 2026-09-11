using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal static class NativePipe
{
    internal static string UserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new IOException("Token user unavailable.");
        }
    }

    internal static uint SessionId
    {
        get { using var process = Process.GetCurrentProcess(); return (uint)process.SessionId; }
    }

    internal static string EndpointName => "AssetLibrary.ExplorerProof.v1." + UserSid + "." + SessionId.ToString(CultureInfo.InvariantCulture);

    internal static NamedPipeServerStream Create(string name, bool first)
    {
        // CurrentUserOnly compares token OWNER on affected runtimes. The actual TokenUser SID is the boundary here.
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

    internal static bool IsCurrentSession(SafePipeHandle pipe) =>
        GetNamedPipeClientSessionId(pipe, out var session) && session == SessionId;

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
}
