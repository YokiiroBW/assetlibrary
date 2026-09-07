using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialCertificate
{
    public static async ValueTask<X509Certificate2> LoadAsync(TrialConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var password = (await TrialPrivateState.ReadTextAsync(
            configuration.TlsCertificatePasswordFile, 4096, cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n');
        TrialPrivateState.RequireSafePath(configuration.TlsCertificateFile);
        TrialPrivateState.RequirePrivateFile(configuration.TlsCertificateFile);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await using var stream = new FileStream(
            configuration.TlsCertificateFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        const int maximumCertificateBytes = 1024 * 1024;
        if (stream.Length > maximumCertificateBytes)
        {
            throw new TrialConfigurationException("trial_certificate_invalid");
        }

        var bytes = new byte[maximumCertificateBytes + 1];
        try
        {
            var length = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, deadline.Token)
                .ConfigureAwait(false);
            if (length > maximumCertificateBytes)
            {
                throw new TrialConfigurationException("trial_certificate_invalid");
            }

            var certificate = X509CertificateLoader.LoadPkcs12(
                bytes.AsSpan(0, length), password.AsSpan(),
                OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey
                || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow
                || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow
                || !certificate.MatchesHostname(configuration.Origin.IdnHost))
            {
                certificate.Dispose();
                throw new TrialConfigurationException("trial_certificate_invalid");
            }

            return certificate;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
