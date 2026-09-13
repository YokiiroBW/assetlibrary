using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AssetLibrary.ImageSupervisor;

[SupportedOSPlatform("linux")]
internal static class LinuxImagePeer
{
    internal static bool IsCore(Socket socket)
    {
        uint length = 12;
        return Native.GetOption(checked((int)socket.Handle), 1, 17, out var peer, ref length) == 0 && length == 12 && peer.User == 1654;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Credentials { internal int ProcessId; internal uint User; internal uint Group; }
    private static class Native
    {
        [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int GetOption(int descriptor, int level, int name, out Credentials result, ref uint length);
    }
}
