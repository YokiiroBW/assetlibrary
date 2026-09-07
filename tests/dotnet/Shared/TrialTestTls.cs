using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AssetLibrary.IntegrationTestSupport;

internal static class TrialTestTls
{
    public static byte[] CreatePkcs12(string? password = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        return generated.Export(X509ContentType.Pkcs12, password);
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
