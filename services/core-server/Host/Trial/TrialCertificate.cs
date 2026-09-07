using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialCertificate
{
    public static async ValueTask<X509Certificate2> LoadAsync(TrialConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var certificate = await LoadFileAsync(configuration.TlsCertificateFile, configuration.TlsCertificatePasswordFile,
            forTls: true, cancellationToken).ConfigureAwait(false);
        if (certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow
            || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow
            || !certificate.MatchesHostname(configuration.Origin.IdnHost))
        {
            certificate.Dispose();
            throw new TrialConfigurationException("trial_certificate_invalid");
        }

        return certificate;
    }

    internal static async ValueTask<X509Certificate2> LoadFileAsync(string path, string passwordPath, bool forTls, CancellationToken cancellationToken)
    {
        var password = (await TrialPrivateState.ReadTextAsync(
            passwordPath, 4096, cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n');
        TrialPrivateState.RequireSafePath(path);
        TrialPrivateState.RequirePrivateFile(path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
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
                OperatingSystem.IsWindows() && forTls ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new TrialConfigurationException("trial_certificate_invalid");
            }

            return certificate;
        }
        catch (CryptographicException)
        {
            throw new TrialConfigurationException("trial_certificate_invalid");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

internal sealed class TrialCertificateHistory : IDisposable
{
    private readonly List<X509Certificate2> certificates = [];
    public IReadOnlyList<X509Certificate2> Certificates => certificates;

    public static async ValueTask<TrialCertificateHistory> LoadAsync(TrialConfiguration configuration, CancellationToken cancellationToken)
    {
        var history = new TrialCertificateHistory();
        try
        {
            foreach (var item in configuration.DecryptionCertificates)
            {
                history.certificates.Add(await TrialCertificate.LoadFileAsync(item.CertificateFile, item.PasswordFile,
                    forTls: false, cancellationToken).ConfigureAwait(false));
            }

            return history;
        }
        catch
        {
            history.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var certificate in certificates)
        {
            certificate.Dispose();
        }
    }
}
