using System.Net.Sockets;
using System.Runtime.Versioning;
using AssetLibrary.ImagePreview.Isolation;

namespace AssetLibrary.ImageSupervisor;

[SupportedOSPlatform("linux")]
internal static class SupervisorStartup
{
    internal static Socket Bind()
    {
        if (!LinuxContainerIdentity.Supervisor() || !LinuxSupervisorFiles.DirectoryIsPrivate() || !LinuxContainerMemory.IsBounded())
            throw new IOException("Supervisor identity or directory rejected.");
        LinuxSupervisorFiles.RemoveOwnedSocket();
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(LinuxSupervisorFiles.SocketPath));
            LinuxSupervisorFiles.SecureNewSocket();
            listener.Listen(1); return listener;
        }
        catch { listener.Dispose(); throw; }
    }
    internal static bool Healthy()
    {
        if (!LinuxSupervisorFiles.DirectoryIsPrivate() || !LinuxSupervisorFiles.SocketIsPrivate() || !ImageCircuitLedger.Healthy()) return false;
        if (!LinuxContainerStatus.AllThreads("/proc/1/task", 0, 225) || !LinuxContainerMemory.IsBounded()) return false;
        if (new FileInfo("/proc/1/exe").LinkTarget != "/app/image-supervisor/AssetLibrary.ImageSupervisor") return false;
        // A stale inode is not a listening service. This does not connect or launch a decoder.
        var lines = File.ReadLines("/proc/net/unix").Take(4096);
        return lines.Any(line =>
        {
            var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return columns.Length == 8 && columns[3] == "00010000" && columns[4] == "0001"
                && columns[7] == LinuxSupervisorFiles.SocketPath;
        });
    }
}
