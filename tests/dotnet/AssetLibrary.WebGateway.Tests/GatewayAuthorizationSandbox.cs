using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace AssetLibrary.WebGateway.Tests;

internal sealed class GatewayAuthorizationSandbox : IDisposable
{
    public GatewayAuthorizationSandbox()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AssetLibrary-V01-016", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        if (OperatingSystem.IsWindows())
        {
            MakePrivateOnWindows(Path);
        }
        else
        {
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        using var rsa = RSA.Create(2048);
        var certificate = new CertificateRequest("CN=AssetLibrary V01-016 test", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Certificate = certificate.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        Configuration = new GatewayAuthorizationConfiguration(System.IO.Path.Combine(Path, "operator.key"), Guid.NewGuid());
    }

    public string Path { get; }

    public X509Certificate2 Certificate { get; }

    public GatewayAuthorizationConfiguration Configuration { get; }

    public IDataProtectionProvider Protection() => DataProtectionProvider.Create(
        new DirectoryInfo(System.IO.Path.Combine(Path, "data-protection")),
        builder => builder.SetApplicationName("V01-016-test").ProtectKeysWithCertificate(Certificate));

    public async ValueTask<ProtectedGatewayAuthorizationKeyStore> InitializeKeysAsync(TimeProvider clock)
    {
        var keys = new ProtectedGatewayAuthorizationKeyStore(Configuration, Protection(), clock);
        await keys.InitializeAsync(CancellationToken.None);
        return keys;
    }

    public void Dispose()
    {
        Certificate.Dispose();
        Directory.Delete(Path, recursive: true);
    }

    [SupportedOSPlatform("windows")]
    private static void MakePrivateOnWindows(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
}
