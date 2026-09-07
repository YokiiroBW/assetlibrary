using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AssetLibrary.CoreServer.Hosting.Trial;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using static AssetLibrary.Packaging.Tests.TrialCertificateTestSupport;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class TrialCertificatePolicyTests
{
    [TestMethod]
    public async Task EcdsaTlsKeysAreRejectedBeforeDataProtectionCanFail()
    {
        using var fixture = new TrialConfigurationFixture();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var files = WriteCertificate(fixture, certificate, "ecdsa");

        var error = await Assert.ThrowsExactlyAsync<TrialConfigurationException>(async () =>
            await TrialCertificate.LoadFileAsync(files.CertificateFile, files.PasswordFile, false, CancellationToken.None));

        Assert.AreEqual("trial_certificate_requires_rsa", error.Code);
    }

    [TestMethod]
    public async Task CertificateRenewalCanDecryptExistingDataWithAnExplicitRetainedKey()
    {
        using var fixture = new TrialConfigurationFixture();
        using var oldKey = RSA.Create(2048);
        using var newKey = RSA.Create(2048);
        var before = new CertificateRequest("CN=localhost", oldKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var after = new CertificateRequest("CN=localhost", newKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var oldCertificate = before.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));
        using var newCertificate = after.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var files = WriteCertificate(fixture, oldCertificate, "previous");
        var configuration = fixture.Configuration with { DecryptionCertificates = [files] };
        using var history = await TrialCertificateHistory.LoadAsync(configuration, CancellationToken.None);
        string protectedValue;
        using (var previous = Protection(configuration, oldCertificate))
        {
            protectedValue = previous.GetRequiredService<IDataProtectionProvider>().CreateProtector("trial-renewal")
                .Protect("synthetic persisted authorization data");
        }

        using (var missingHistory = Protection(configuration, newCertificate))
        {
            Assert.Throws<CryptographicException>(() => missingHistory.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("trial-renewal").Unprotect(protectedValue));
        }

        using var renewed = Protection(configuration, newCertificate, history.Certificates.ToArray());
        var cleartext = renewed.GetRequiredService<IDataProtectionProvider>().CreateProtector("trial-renewal").Unprotect(protectedValue);

        Assert.AreEqual("synthetic persisted authorization data", cleartext);
        Assert.IsNotEmpty(Directory.GetFiles(configuration.DataProtectionPath, "key-*.xml"));
    }

    [TestMethod]
    public void CertificateHistoryHasABoundAndCannotReferenceAssetStorage()
    {
        using var fixture = new TrialConfigurationFixture();
        var files = new TrialDecryptionCertificate { CertificateFile = fixture.AssetPath, PasswordFile = fixture.AssetPath };
        Assert.ThrowsExactly<TrialConfigurationException>(() => TrialConfigurationValidator.Validate(
            fixture.Configuration with { DecryptionCertificates = [files] }, fixture.ConfigurationPath));
        Assert.ThrowsExactly<TrialConfigurationException>(() => TrialConfigurationValidator.Validate(
            fixture.Configuration with { DecryptionCertificates = [files, files, files, files] }, fixture.ConfigurationPath));
    }

}

internal static class TrialCertificateTestSupport
{
    public static ServiceProvider Protection(TrialConfiguration configuration, X509Certificate2 certificate,
        params X509Certificate2[] previous)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("AssetLibrary:" + configuration.DeploymentId.ToString("D"))
            .PersistKeysToFileSystem(new DirectoryInfo(configuration.DataProtectionPath))
            .ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate([certificate, .. previous]);
        return services.BuildServiceProvider();
    }

    public static TrialDecryptionCertificate WriteCertificate(TrialConfigurationFixture fixture, X509Certificate2 certificate, string name)
    {
        var files = new TrialDecryptionCertificate
        {
            CertificateFile = Path.Combine(fixture.Configuration.StatePath, name + ".pfx"),
            PasswordFile = Path.Combine(fixture.Configuration.StatePath, name + ".password"),
        };
        const string password = "synthetic certificate password";
        var bytes = certificate.Export(X509ContentType.Pkcs12, password);
        try
        {
            File.WriteAllBytes(files.CertificateFile, bytes);
            File.WriteAllText(files.PasswordFile, password);
            TrialConfigurationFixture.MakePrivateFile(files.CertificateFile);
            TrialConfigurationFixture.MakePrivateFile(files.PasswordFile);
            return files;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
