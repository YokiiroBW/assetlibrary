using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using AssetLibrary.CoreServer.Hosting.Trial;

namespace AssetLibrary.Packaging.Tests;

internal sealed class TrialConfigurationFixture : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly TemporaryDirectory temporary = TemporaryDirectory.Create();

    public TrialConfigurationFixture()
    {
        var state = Path.Combine(temporary.Path, "state");
        var keys = Path.Combine(state, "keys");
        var web = Path.Combine(temporary.Path, "web");
        var assets = Path.Combine(temporary.Path, "assets");
        Directory.CreateDirectory(state);
        MakePrivateDirectory(state);
        Directory.CreateDirectory(keys);
        MakePrivateDirectory(keys);
        Directory.CreateDirectory(web);
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(web, "index.html"), "<html></html>");
        AssetPath = Path.Combine(assets, "sample.txt");
        File.WriteAllText(AssetPath, "read-only fixture");
        Configuration = new TrialConfiguration
        {
            FormatVersion = 1,
            DeploymentId = Guid.NewGuid(),
            PublicOrigin = "https://localhost:7443",
            StatePath = state,
            TlsCertificateFile = Path.Combine(state, "server.pfx"),
            TlsCertificatePasswordFile = Path.Combine(state, "server.password"),
            DataProtectionPath = keys,
            AuthorizationKeyFile = Path.Combine(state, "authorization.key"),
            WebRoot = web,
            Database = new TrialDatabaseFiles
            {
                AuditConnectionFile = Path.Combine(state, "audit.connection"),
                GatewayConnectionFile = Path.Combine(state, "gateway.connection"),
                LibraryConnectionFile = Path.Combine(state, "library.connection"),
                AssetConnectionFile = Path.Combine(state, "asset.connection"),
                ScanConnectionFile = Path.Combine(state, "scan.connection"),
                TaskConnectionFile = Path.Combine(state, "task.connection"),
            },
            StorageSources = [new TrialStorageSource
            {
                SourceKey = "test-assets", DisplayName = "Test assets", StorageSourceId = Guid.NewGuid(),
                AllowedRoot = assets, CaseSensitive = !OperatingSystem.IsWindows(),
            }],
        };
        ConfigurationPath = Path.Combine(state, "trial.json");
        WriteConfiguration(Configuration);
    }

    public TrialConfiguration Configuration { get; }
    public string ConfigurationPath { get; }
    public string AssetPath { get; }

    public void WriteConfiguration(TrialConfiguration configuration)
    {
        File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(configuration, SerializerOptions));
        MakePrivateFile(ConfigurationPath);
    }

    public static void MakePrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new FileSecurity();
            security.SetOwner(identity.User!);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void MakePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new DirectorySecurity();
            security.SetOwner(identity.User!);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public void Dispose() => temporary.Dispose();
}
