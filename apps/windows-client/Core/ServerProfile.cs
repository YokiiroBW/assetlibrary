using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AssetLibrary.Windows.Client;

public sealed record ServerProfile
{
    public ServerProfile(string address, string? certificateSha256 = null)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.AbsolutePath != "/"
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            throw new ArgumentException("请输入完整 HTTPS 服务器地址，不包含路径、账号或查询参数。", nameof(address));
        }

        var pin = certificateSha256?.Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(pin) && (pin.Length != 64 || !pin.All(Uri.IsHexDigit)))
        {
            throw new ArgumentException("证书指纹必须为 64 位 SHA-256 十六进制值。", nameof(certificateSha256));
        }

        Endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/");
        CertificateSha256 = string.IsNullOrEmpty(pin) ? null : pin.ToUpperInvariant();
    }

    public Uri Endpoint { get; }
    public string? CertificateSha256 { get; }
    public string Origin => Endpoint.GetLeftPart(UriPartial.Authority);

    public bool AcceptCertificate(X509Certificate2? certificate, SslPolicyErrors errors, DateTimeOffset now)
    {
        if (certificate is null || now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
        {
            return false;
        }

        if (CertificateSha256 is null)
        {
            return errors == SslPolicyErrors.None;
        }

        if ((errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0
            || !certificate.MatchesHostname(Endpoint.IdnHost, allowWildcards: true, allowCommonName: false))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(CertificateSha256), certificate.GetCertHash(HashAlgorithmName.SHA256));
    }
}
