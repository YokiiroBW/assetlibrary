using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using AssetLibrary.Windows.AssetHost;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class HostPipeSecurityTests
{
    [TestMethod]
    public void RealPipeDaclContainsOnlyActualTokenUser()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows security descriptors are required."); }
        using var pipe = NativePipe.Create(NativePipe.EndpointName + ".test." + Guid.NewGuid().ToString("N"), true);
        var descriptor = ReadDescriptor(pipe.SafePipeHandle);
        Assert.IsNotNull(descriptor.DiscretionaryAcl);
        Assert.HasCount(1, descriptor.DiscretionaryAcl);
        var ace = (CommonAce)descriptor.DiscretionaryAcl[0];
        Assert.AreEqual(NativePipe.UserSid, ace.SecurityIdentifier.Value);
        Assert.AreEqual(AceQualifier.AccessAllowed, ace.AceQualifier);
        Assert.IsTrue(descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected));
    }

    [SupportedOSPlatform("windows")]
    private static RawSecurityDescriptor ReadDescriptor(SafePipeHandle handle)
    {
        Assert.AreEqual(0u, GetSecurityInfo(handle, 6, 4, out _, out _, out _, out _, out var security));
        try
        {
            var bytes = new byte[GetSecurityDescriptorLength(security)];
            Marshal.Copy(security, bytes, 0, bytes.Length);
            return new RawSecurityDescriptor(bytes, 0);
        }
        finally { _ = LocalFree(security); }
    }

    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetSecurityInfo(SafePipeHandle handle, int objectType, uint information,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr descriptor);
}
