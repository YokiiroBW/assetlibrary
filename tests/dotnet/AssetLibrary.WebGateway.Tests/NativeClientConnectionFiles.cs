using System.Security.Cryptography;
using System.Text.Json;

namespace AssetLibrary.WebGateway.Tests;

internal static class NativeClientConnectionFiles
{
    public static async Task<string> PublishAsync(TrialHostIntegrationSettings settings, TrialHostIntegrationFixture host,
        Guid libraryId, int fileCount, DateTimeOffset expiresAt)
    {
        var privateDirectory = Path.Combine(settings.RuntimeRoot, "native-client-private");
        TrialHostIntegrationPrivateDirectory.Create(privateDirectory);
        var connectionFile = Path.Combine(privateDirectory, "connection.json");
        var stopFile = Path.Combine(privateDirectory, "stop");
        var fingerprint = host.Certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var serializer = TrialHostIntegrationSettings.Serialization;
        await File.WriteAllTextAsync(connectionFile, JsonSerializer.Serialize(new
        {
            origin = host.Configuration.PublicOrigin,
            certificate_sha256 = fingerprint,
            account_name = "trial-admin",
            password = TrialHostIntegrationAuthentication.Password,
            invisible_account_name = "trial-reader",
            invisible_account_password = TrialHostIntegrationAuthentication.Password,
            library_id = libraryId,
            sample_file_count = fileCount,
            expires_at = expiresAt,
        }, serializer));
        var ready = JsonSerializer.Serialize(new
        {
            origin = host.Configuration.PublicOrigin,
            certificate_sha256 = fingerprint,
            connection_file = connectionFile,
            stop_file = stopFile,
            host_process_id = Environment.ProcessId,
            expires_at = expiresAt,
        }, serializer);
        var readyFile = Path.Combine(settings.Evidence, "ready.json");
        await File.WriteAllTextAsync(readyFile + ".pending", ready);
        File.Move(readyFile + ".pending", readyFile);
        return stopFile;
    }
}
