using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationPrivateDirectory
{
    public static void Create(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            CreateWindows(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateWindows(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm($"O:{sid}D:P(A;OICI;FA;;;{sid})(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)");
        new DirectoryInfo(path).Create(security);
    }
}
