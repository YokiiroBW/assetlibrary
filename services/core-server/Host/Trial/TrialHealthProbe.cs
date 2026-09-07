using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialHealthProbe
{
    public static async Task<int> RunAsync(string configurationPath)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var configuration = await TrialConfiguration.LoadAsync(configurationPath, deadline.Token).ConfigureAwait(false);
            using var expected = await TrialCertificate.LoadAsync(configuration, deadline.Token).ConfigureAwait(false);
            using var handler = new HttpClientHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, peer, _, errors) =>
                    MatchesConfiguredCertificate(peer, expected, errors),
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await client.GetAsync(new Uri(configuration.Origin, "/readyz"),
                HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (int)CoreServerExitCode.Unavailable;
            }

            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var bytes = new byte[4097];
            var count = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, deadline.Token).ConfigureAwait(false);
            if (count == bytes.Length)
            {
                return (int)CoreServerExitCode.Unavailable;
            }

            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            var root = document.RootElement;
            var healthy = root.GetProperty("contract").GetString() == "v01-015/1"
                && root.GetProperty("status").GetString() == "ready"
                && root.GetProperty("deployment_id").GetGuid() == configuration.DeploymentId
                && root.GetProperty("business_api_ready").GetBoolean()
                && !root.GetProperty("production_file_writes_enabled").GetBoolean();
            return healthy ? 0 : (int)CoreServerExitCode.Unavailable;
        }
        catch (Exception)
        {
            return (int)CoreServerExitCode.Unavailable;
        }
    }

    private static bool MatchesConfiguredCertificate(X509Certificate2? peer, X509Certificate2 expected, SslPolicyErrors errors) =>
        peer is not null && (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) == 0
        && CryptographicOperations.FixedTimeEquals(peer.GetCertHash(HashAlgorithmName.SHA256), expected.GetCertHash(HashAlgorithmName.SHA256));
}
