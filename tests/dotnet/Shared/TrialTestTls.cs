using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AssetLibrary.IntegrationTestSupport;

internal static class TrialTestTls
{
    public static byte[] CreatePkcs12(string? password = null, TimeSpan? validity = null)
    {
        using var generated = CreateSelfSigned(validity: validity);
        return generated.Export(X509ContentType.Pkcs12, password);
    }

    public static X509Certificate2 CreateSelfSigned(string hostname = "localhost", TimeSpan? validity = null)
    {
        var duration = validity ?? TimeSpan.FromHours(1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(duration, TimeSpan.FromHours(3));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={hostname}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(hostname);
        request.CertificateExtensions.Add(names.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.Add(duration));
    }

    public static X509Certificate2 Certificate()
    {
        var bytes = CreatePkcs12();
        try
        {
            return X509CertificateLoader.LoadPkcs12(bytes, null,
                OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static Uri ReserveOrigin()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return new Uri($"https://localhost:{port}");
    }

    public static HttpClient Client(X509Certificate2 certificate, Uri origin)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                presented is not null && presented.Thumbprint == certificate.Thumbprint,
        };
        return new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(10) };
    }
}
