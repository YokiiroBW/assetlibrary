using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetLibrary.Windows.Setup;

namespace AssetLibrary.Windows.Setup.Tests;

[TestClass]
public sealed class InstallerTests : SetupTestBase
{
    [TestMethod]
    public async Task InstallReinstallUpgradeAndUninstallPreserveSourceAndUserConfiguration()
    {
        string first = await PackageAsync("0.3.0-preview.1");
        string second = await PackageAsync("0.3.0-preview.2");
        string config = Path.Combine(Sandbox, "private-settings.json");
        await File.WriteAllTextAsync(config, "synthetic configuration");
        List<string> stopped = [];
        Installer installer = CreateInstaller((directory, _) => { stopped.Add(directory); return Task.CompletedTask; });
        SetupReport installed = await installer.InstallAsync(first, CancellationToken.None);
        Assert.AreEqual("installed", installed.Status);
        Assert.AreEqual(installed.InstallDir, RegistrationPlan.InstallDirectory(Store.Read()));
        Assert.AreEqual("installed", (await installer.InstallAsync(first, CancellationToken.None)).Status);
        SetupReport upgraded = await installer.InstallAsync(second, CancellationToken.None);
        Assert.AreEqual("0.3.0-preview.2", upgraded.Version);
        Assert.IsTrue(Directory.Exists(installed.InstallDir));
        Assert.HasCount(2, stopped);
        Assert.AreEqual("uninstalled", (await installer.UninstallAsync(CancellationToken.None)).Status);
        Assert.IsEmpty(Store.Read().Keys);
        Assert.IsEmpty(Directory.GetDirectories(Path.Combine(Root, "versions")));
        Assert.AreEqual("synthetic configuration", await File.ReadAllTextAsync(config));
        Assert.IsTrue(File.Exists(Path.Combine(first, "payload", "AssetLibrary.Host.exe")));
        Assert.AreEqual("uninstalled", (await installer.UninstallAsync(CancellationToken.None)).Status);
    }

    [TestMethod]
    public async Task SameVersionDifferentHashRefusesOverwrite()
    {
        string first = await PackageAsync();
        Installer installer = CreateInstaller();
        SetupReport original = await installer.InstallAsync(first, CancellationToken.None);
        string second = await PackageAsync(flavor: "changed");
        SetupException error = await Assert.ThrowsExactlyAsync<SetupException>(() => installer.InstallAsync(second, CancellationToken.None));
        Assert.AreEqual("version_conflict", error.Code);
        Assert.AreEqual(original.InstallDir, RegistrationPlan.InstallDirectory(Store.Read()));
        Assert.AreEqual("synthetic-AssetLibrary.Host.exe", await File.ReadAllTextAsync(Path.Combine(original.InstallDir!, "AssetLibrary.Host.exe")));
    }

    [TestMethod]
    public async Task ForeignDirectoryAndRegistryArePreserved()
    {
        string package = await PackageAsync();
        Directory.CreateDirectory(Root);
        string foreign = Path.Combine(Root, "do-not-touch.txt");
        await File.WriteAllTextAsync(foreign, "foreign");
        SetupException error = await Assert.ThrowsExactlyAsync<SetupException>(() => CreateInstaller().InstallAsync(package, CancellationToken.None));
        Assert.AreEqual("foreign_owner", error.Code);
        Assert.AreEqual("foreign", await File.ReadAllTextAsync(foreign));
        StateFile.Write(Path.Combine(Root, Product.OwnerFile), new Ownership(Product.Owner));
        RegistrationState state = RegistrationPlan.Create(Path.Combine(Root, "versions", "old"), "old");
        state.Keys[RegistrationPlan.ClassKey]["AssetLibraryOwner"] = new("foreign");
        Store.Write(state);
        await Assert.ThrowsExactlyAsync<SetupException>(() => CreateInstaller().InstallAsync(package, CancellationToken.None));
        Assert.AreEqual("foreign", Store.Read().Keys[RegistrationPlan.ClassKey]["AssetLibraryOwner"].Text);
    }

    [TestMethod]
    public async Task LowSpaceAndCanceledInstallDoNotSwitchRegistration()
    {
        string package = await PackageAsync();
        Installer installer = new(Root, Store, static (_, _) => Task.CompletedTask, () => 1);
        SetupException error = await Assert.ThrowsExactlyAsync<SetupException>(() => installer.InstallAsync(package, CancellationToken.None));
        Assert.AreEqual("insufficient_space", error.Code);
        Assert.IsEmpty(Store.Read().Keys);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateInstaller().InstallAsync(package, canceled.Token));
        Assert.IsEmpty(Store.Read().Keys);
    }

}
