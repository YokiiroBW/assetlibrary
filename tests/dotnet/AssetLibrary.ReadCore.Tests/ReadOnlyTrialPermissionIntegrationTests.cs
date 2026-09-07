using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialPermissionIntegrationTests
{
    [TestMethod]
    public async Task ARealDirectoryPermissionDenialIsOfflineAndRecoversWithoutIndexDeletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This test validates the Windows x64 trial ACL boundary.");
            return;
        }

        await VerifyWindowsAsync();
    }

    [SupportedOSPlatform("windows")]
    private static async Task VerifyWindowsAsync()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var library = await fixture.RegisterAsync();
        var directory = new DirectoryInfo(Path.Combine(fixture.Sandbox.Root, "library"));
        var original = directory.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var denied = directory.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
        directory.SetAccessControl(denied);
        try
        {
            Assert.AreEqual(StorageAvailability.Offline, await fixture.Availability.RefreshAsync(library, CancellationToken.None));
            Assert.IsNull(await fixture.Snapshots.FindAsync(library, CancellationToken.None));
        }
        finally
        {
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            directory.SetAccessControl(restored);
        }

        Assert.AreEqual(StorageAvailability.Online, await fixture.Availability.RefreshAsync(library, CancellationToken.None));
    }
}
