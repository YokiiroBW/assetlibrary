using System.Security.Cryptography;
using System.Text.Json;
using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.IntegrationTestSupport;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationConfiguration
{
    public static async Task<TrialConfiguration> CreateAsync(TrialHostIntegrationSettings settings, TimeSpan? certificateValidity = null)
    {
        var state = Path.Combine(settings.RuntimeRoot, "state");
        TrialHostIntegrationPrivateDirectory.Create(state);
        var keys = Path.Combine(state, "keys");
        TrialHostIntegrationPrivateDirectory.Create(keys);
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var certificateFile = Path.Combine(state, "tls.pfx");
        var passwordFile = Path.Combine(state, "tls-password");
        var certificate = TrialTestTls.CreatePkcs12(password, certificateValidity);
        try
        {
            await File.WriteAllBytesAsync(certificateFile, certificate);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(certificate);
        }

        await File.WriteAllTextAsync(passwordFile, password);
        var paths = new Dictionary<string, string>();
        foreach (var module in settings.Logins.Keys)
        {
            var path = Path.Combine(state, module + ".connection");
            await File.WriteAllTextAsync(path, settings.Connection(module));
            paths.Add(module, path);
        }

        var configuration = new TrialConfiguration
        {
            FormatVersion = 1,
            DeploymentId = Guid.NewGuid(),
            PublicOrigin = TrialTestTls.ReserveOrigin().GetLeftPart(UriPartial.Authority),
            StatePath = state,
            DataProtectionPath = keys,
            AuthorizationKeyFile = Path.Combine(state, "operator.key"),
            TlsCertificateFile = certificateFile,
            TlsCertificatePasswordFile = passwordFile,
            WebRoot = settings.WebRoot,
            Database = new TrialDatabaseFiles
            {
                AuditConnectionFile = paths["audit"],
                GatewayConnectionFile = paths["gateway"],
                LibraryConnectionFile = paths["library"],
                AssetConnectionFile = paths["asset"],
                ScanConnectionFile = paths["scan"],
                TaskConnectionFile = paths["task"],
            },
            StorageSources = [new TrialStorageSource
            {
                SourceKey = "fixtures", DisplayName = "隔离试用存储", StorageSourceId = Guid.NewGuid(),
                AllowedRoot = Path.Combine(settings.RuntimeRoot, "assets"), CaseSensitive = !OperatingSystem.IsWindows(),
            }],
        };
        var configurationPath = Path.Combine(state, "trial.json");
        await File.WriteAllTextAsync(configurationPath,
            JsonSerializer.Serialize(configuration, TrialHostIntegrationSettings.Serialization));
        if (!OperatingSystem.IsWindows())
        {
            // The parent is already private; set explicit file permissions without relying on the caller's umask.
            foreach (var file in paths.Values.Append(certificateFile).Append(passwordFile).Append(configurationPath))
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        return await TrialConfiguration.LoadAsync(configurationPath, CancellationToken.None);
    }
}
