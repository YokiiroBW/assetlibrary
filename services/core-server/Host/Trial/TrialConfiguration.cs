using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal sealed record TrialConfiguration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    };

    public required int FormatVersion { get; init; }
    public required Guid DeploymentId { get; init; }
    public required string PublicOrigin { get; init; }
    public bool ServiceReadEnabled { get; init; }
    public int ServiceReadMaximumLifetimeDays { get; init; } = 365;
    public string BindHost { get; init; } = "127.0.0.1";
    public required string StatePath { get; init; }
    public required string TlsCertificateFile { get; init; }
    public required string TlsCertificatePasswordFile { get; init; }
    public TrialDecryptionCertificate[] DecryptionCertificates { get; init; } = [];
    public required string DataProtectionPath { get; init; }
    public required string AuthorizationKeyFile { get; init; }
    public required string WebRoot { get; init; }
    public required TrialDatabaseFiles Database { get; init; }
    public required TrialStorageSource[] StorageSources { get; init; }

    [JsonIgnore]
    public Uri Origin => new(PublicOrigin, UriKind.Absolute);

    [JsonIgnore]
    public IPAddress BindAddress => IPAddress.Parse(BindHost);

    public override string ToString() => "[read-only trial configuration]";

    public static async ValueTask<TrialConfiguration> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var json = await TrialPrivateState.ReadTextAsync(path, 64 * 1024, cancellationToken).ConfigureAwait(false);
        TrialConfiguration configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<TrialConfiguration>(json, SerializerOptions)
                ?? throw new TrialConfigurationException("trial_configuration_invalid");
        }
        catch (JsonException)
        {
            throw new TrialConfigurationException("trial_configuration_invalid");
        }
        TrialConfigurationValidator.Validate(configuration, path);
        return configuration;
    }
}

internal sealed record TrialDatabaseFiles
{
    public required string AuditConnectionFile { get; init; }
    public required string GatewayConnectionFile { get; init; }
    public required string LibraryConnectionFile { get; init; }
    public required string AssetConnectionFile { get; init; }
    public required string ScanConnectionFile { get; init; }
    public required string TaskConnectionFile { get; init; }

    public IEnumerable<string> Paths() => [
        AuditConnectionFile, GatewayConnectionFile, LibraryConnectionFile,
        AssetConnectionFile, ScanConnectionFile, TaskConnectionFile,
    ];

    public override string ToString() => "[database connection files]";
}

internal sealed record TrialStorageSource
{
    public required string SourceKey { get; init; }
    public required string DisplayName { get; init; }
    public required Guid StorageSourceId { get; init; }
    public required string AllowedRoot { get; init; }
    public required bool CaseSensitive { get; init; }

    public override string ToString() => "[configured storage source]";
}

internal sealed record TrialDecryptionCertificate
{
    public required string CertificateFile { get; init; }
    public required string PasswordFile { get; init; }
}

internal sealed class TrialConfigurationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
