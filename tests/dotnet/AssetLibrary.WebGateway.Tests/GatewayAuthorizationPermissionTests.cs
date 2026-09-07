using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using static AssetLibrary.WebGateway.Tests.GatewayAuthorizationKeyTests;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class GatewayAuthorizationPermissionTests
{
    [TestMethod]
    public async Task ReadableByOtherUsersKeyDirectoryFailsClosedWithoutCredentialOrKeyMutation()
    {
        using var sandbox = new GatewayAuthorizationSandbox();
        var clock = new MutableGatewayTimeProvider(DateTimeOffset.UtcNow);
        var keys = await sandbox.InitializeKeysAsync(clock);
        var authorizer = new FileOutOfBandAuthorization(keys, sandbox.Configuration.DeploymentId, clock);
        var request = Request(clock.GetUtcNow());
        using var proof = await authorizer.IssueAsync(request, CancellationToken.None);
        var before = await File.ReadAllBytesAsync(sandbox.Configuration.KeyFilePath);
        if (OperatingSystem.IsWindows())
        {
            MakeReadableOnWindows(sandbox.Path);
        }
        else
        {
            File.SetUnixFileMode(sandbox.Path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
        }

        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Unavailable,
            (await authorizer.VerifyAsync(ToVerification(request), proof, CancellationToken.None)).Status);
        await Assert.ThrowsExactlyAsync<IOException>(() => keys.RotateAsync(CancellationToken.None).AsTask());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(sandbox.Configuration.KeyFilePath));
    }

    [SupportedOSPlatform("windows")]
    private static void MakeReadableOnWindows(string path)
    {
        var directory = new DirectoryInfo(path);
        var access = directory.GetAccessControl();
        access.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        directory.SetAccessControl(access);
    }
}
