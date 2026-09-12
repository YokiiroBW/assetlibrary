using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetLibrary.Windows.Setup;

namespace AssetLibrary.Windows.Setup.Tests;

[TestClass]
public abstract class SetupTestBase
{
    protected string Sandbox { get; private set; } = "";
    protected string Root => Path.Combine(Sandbox, "installation");
    protected string RegistryPath => Path.Combine(Sandbox, "registry.json");
    private protected SandboxRegistrationStore Store => new(RegistryPath);

    [TestInitialize]
    public void Initialize()
    {
        Sandbox = Path.Combine(Path.GetTempPath(), "AssetLibrary-setup-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Sandbox);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SafePath.RequireInside(Path.GetTempPath(), Sandbox);
        if (Directory.Exists(Sandbox)) { Directory.Delete(Sandbox, true); }
    }

    private protected Installer CreateInstaller(Func<string, CancellationToken, Task>? stop = null) =>
        new(Root, Store, stop ?? (static (_, _) => Task.CompletedTask));

    private protected static Task<PackageManifest> ManifestAsync(string package) =>
        PackageManifest.ReadAsync(Path.Combine(package, Product.ManifestName), CancellationToken.None);

    protected async Task<string> PackageAsync(string version = Product.Version, string flavor = "synthetic")
    {
        string package = Path.Combine(Sandbox, "package-" + Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(package, "payload");
        Directory.CreateDirectory(payload);
        List<PayloadFile> files = [];
        foreach (string name in new[] { "AssetLibrary.Setup.exe", "AssetLibrary.Host.exe", "AssetLibrary.Settings.exe", "AssetLibrary.Explorer.dll" })
        {
            byte[] bytes = Encoding.UTF8.GetBytes(flavor + "-" + name);
            await File.WriteAllBytesAsync(Path.Combine(payload, name), bytes);
            files.Add(new(name, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        File.Copy(Path.Combine(payload, "AssetLibrary.Setup.exe"), Path.Combine(package, "AssetLibrary.Setup.exe"));
        StateFile.Write(Path.Combine(package, Product.ManifestName), new PackageManifest(1, Product.Owner, version, "win-x64", [.. files]));
        return package;
    }

}
