using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AssetLibrary.Modules.PreviewProvider.Infrastructure;

internal static class UnixSocketImagePeer
{
    public static bool IsRoot(Socket socket)
    {
        if (!OperatingSystem.IsLinux() || socket.AddressFamily != AddressFamily.Unix) return false;
        return ReadRoot(socket);
    }

    [SupportedOSPlatform("linux")]
    private static bool ReadRoot(Socket socket)
    {
        // Linux ucred is pid_t/uid_t/gid_t (three 32-bit fields). The peer PID
        // may be invisible across container PID namespaces; authenticate UID.
        uint length = 12;
        try
        {
            return Native.GetCredentials(socket.SafeHandle, 1, 17, out var credentials, ref length) == 0
                && length == 12 && credentials.UserId == 0;
        }
        catch (Exception failure) when (failure is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credentials
    {
        public int ProcessId;
        public uint UserId;
        public uint GroupId;
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "getsockopt", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int GetCredentials(SafeSocketHandle socket, int level, int option,
            out Credentials credentials, ref uint length);
    }
}
