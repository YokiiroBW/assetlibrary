using System.Net;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialConfigurationValidator
{
    public static void Validate(TrialConfiguration configuration, string configurationPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.FormatVersion != 1 || configuration.DeploymentId == Guid.Empty)
        {
            throw new TrialConfigurationException("trial_configuration_version_invalid");
        }

        ValidateOrigin(configuration.PublicOrigin, configuration.BindHost);
        TrialPrivateState.RequireDirectory(configuration.StatePath);
        TrialPrivateState.RequireContained(configuration.StatePath, configurationPath);
        foreach (var path in StateFiles(configuration))
        {
            TrialPrivateState.RequireContained(configuration.StatePath, path);
            TrialPrivateState.RequireSafePath(path);
        }

        TrialPrivateState.RequireDirectory(configuration.DataProtectionPath);
        TrialPrivateState.RequireSafePath(configuration.WebRoot);
        if (!Directory.Exists(configuration.WebRoot)
            || !File.Exists(Path.Combine(configuration.WebRoot, "index.html"))
            || TrialPrivateState.Overlaps(configuration.StatePath, configuration.WebRoot))
        {
            throw new TrialConfigurationException("trial_web_root_invalid");
        }

        ValidateSources(configuration);
    }

    private static IEnumerable<string> StateFiles(TrialConfiguration configuration)
    {
        if (configuration.Database is null)
        {
            throw new TrialConfigurationException("trial_database_configuration_missing");
        }

        return configuration.Database.Paths().Concat([
            configuration.TlsCertificateFile, configuration.TlsCertificatePasswordFile,
            configuration.DataProtectionPath, configuration.AuthorizationKeyFile,
        ]);
    }

    private static void ValidateOrigin(string value, string bindHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps
            || origin.AbsolutePath != "/"
            || origin.Query.Length != 0
            || origin.Fragment.Length != 0
            || origin.UserInfo.Length != 0
            || origin.Port is < 1024 or > 65535
            || !IPAddress.TryParse(bindHost, out var bindAddress)
            || !(IPAddress.IsLoopback(bindAddress) || bindAddress.Equals(IPAddress.Any) || bindAddress.Equals(IPAddress.IPv6Any)))
        {
            throw new TrialConfigurationException("trial_https_origin_invalid");
        }
    }

    private static void ValidateSources(TrialConfiguration configuration)
    {
        var sources = configuration.StorageSources;
        if (sources is null || sources.Length is < 1 or > 32)
        {
            throw new TrialConfigurationException("trial_storage_sources_invalid");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<Guid>();
        var roots = new List<string>();
        foreach (var source in sources)
        {
            if (source is null || !ValidKey(source.SourceKey)
                || !keys.Add(source.SourceKey) || source.StorageSourceId == Guid.Empty || !ids.Add(source.StorageSourceId)
                || string.IsNullOrWhiteSpace(source.DisplayName) || source.DisplayName.Length > 200
                || source.DisplayName.Any(char.IsControl) || !Path.IsPathFullyQualified(source.AllowedRoot))
            {
                throw new TrialConfigurationException("trial_storage_sources_invalid");
            }

            var root = Path.GetFullPath(source.AllowedRoot);
            if (TrialPrivateState.Overlaps(root, configuration.StatePath)
                || TrialPrivateState.Overlaps(root, configuration.WebRoot)
                || roots.Any(previous => TrialPrivateState.Overlaps(previous, root)))
            {
                throw new TrialConfigurationException("trial_storage_boundary_overlap");
            }

            roots.Add(root);
        }
    }

    private static bool ValidKey(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 64
        && value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '_' or '-');
}
