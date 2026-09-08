using System.Net;
using System.Net.Security;
using System.Security.Cryptography;

namespace AssetLibrary.Preview.Tests;

internal static class LivePreviewTls
{
    public static HttpClient Create(Uri origin, string fingerprint)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) => certificate is not null
                && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
                && certificate.GetCertHashString(HashAlgorithmName.SHA256).Equals(fingerprint, StringComparison.OrdinalIgnoreCase)
                && DateTime.UtcNow >= certificate.NotBefore.ToUniversalTime() && DateTime.UtcNow <= certificate.NotAfter.ToUniversalTime(),
        };
        return new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 12 * 1024 * 1024 };
    }
}
