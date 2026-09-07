using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationCertificateContainer
{
    public static string? Observe(X509Certificate2 certificate) =>
        OperatingSystem.IsWindows() ? ObserveWindows(certificate) : null;

    public static void AssertRemoved(string? path)
    {
        if (path is not null)
        {
            Assert.IsFalse(File.Exists(path), "The owned Windows TLS key container was removed on disposal.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static string ObserveWindows(X509Certificate2 certificate)
    {
        using var key = certificate.GetRSAPrivateKey();
        var crypto = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Crypto");
        string path;
        if (key is RSACng cng)
        {
            path = Path.Combine(crypto, "Keys", cng.Key.UniqueName!);
        }
        else if (key is RSACryptoServiceProvider csp)
        {
            using var identity = WindowsIdentity.GetCurrent();
            path = Path.Combine(crypto, "RSA", identity.User!.Value, csp.CspKeyContainerInfo.UniqueKeyContainerName);
        }
        else
        {
            throw new InvalidOperationException("The test certificate key provider is unknown.");
        }

        Assert.IsTrue(File.Exists(path), "The owned Windows TLS key container must be observable before disposal.");
        return path;
    }
}
