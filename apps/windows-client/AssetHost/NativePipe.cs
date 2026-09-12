using System.IO.Pipes;
using System.Runtime.Versioning;
using AssetLibrary.Windows.Session;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal static class NativePipe
{
    internal static string UserSid => LocalPipe.UserSid;
    internal static uint SessionId => LocalPipe.SessionId;
    internal static string EndpointName => "AssetLibrary.ExplorerProof.v1." + LocalPipe.UserSession;
    internal static NamedPipeServerStream Create(string name, bool first) => LocalPipe.CreateServer(name, first);
    internal static bool IsCurrentSession(SafePipeHandle pipe) => LocalPipe.IsCurrentClientSession(pipe);
}
