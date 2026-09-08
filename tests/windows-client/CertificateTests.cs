using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class CertificateTests
{
    [TestMethod]
    [DataRow("http://fixture.example")]
    [DataRow("https://user:password@fixture.example")]
    [DataRow("https://fixture.example/subpath")]
    [DataRow("https://fixture.example?query=private")]
    [DataRow("https://fixture.example#fragment")]
    public void RejectsOriginsThatCouldMisrouteCredentials(string address) =>
        Assert.ThrowsExactly<ArgumentException>(() => new ServerProfile(address));

    [TestMethod]
    public void RejectsInvalidPinAndNormalizesExplicitOrigin()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ServerProfile("https://fixture.example", "sha1"));
        Assert.AreEqual("https://fixture.example:5443", new ServerProfile("https://fixture.example:5443/").Origin);
    }

    [TestMethod]
    public void PinRequiresExactLeafHostnameAndValidityPeriod()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=fixture.example", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("fixture.example");
        request.CertificateExtensions.Add(names.Build());
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
        var pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var profile = new ServerProfile("https://fixture.example", pin);
        Assert.IsTrue(profile.AcceptCertificate(certificate, SslPolicyErrors.RemoteCertificateChainErrors, now));
        Assert.IsFalse(profile.AcceptCertificate(certificate, SslPolicyErrors.RemoteCertificateNameMismatch, now));
        Assert.IsFalse(profile.AcceptCertificate(certificate, SslPolicyErrors.None, now.AddDays(2)));
        Assert.IsFalse(profile.AcceptCertificate(certificate, SslPolicyErrors.None, now.AddDays(-2)));
        Assert.IsFalse(new ServerProfile("https://other.example", pin).AcceptCertificate(certificate, SslPolicyErrors.None, now));
        Assert.IsFalse(new ServerProfile("https://fixture.example", new string('0', 64)).AcceptCertificate(certificate, SslPolicyErrors.None, now));
        Assert.IsFalse(new ServerProfile("https://fixture.example").AcceptCertificate(certificate, SslPolicyErrors.RemoteCertificateChainErrors, now));
    }
}
