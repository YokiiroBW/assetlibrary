using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetLibrary.Windows.Setup;

namespace AssetLibrary.Windows.Setup.Tests;

[TestClass]
public sealed class RecoveryTests : SetupTestBase
{
    [TestMethod]
    public async Task StagingFailureLeavesOldVersionActiveAndRetrySucceeds()
    {
        string first = await PackageAsync();
        string second = await PackageAsync("0.3.0-preview.2");
        SetupReport old = await CreateInstaller().InstallAsync(first, CancellationToken.None);
        Installer interrupted = new(Root, Store, static (_, _) => Task.CompletedTask, checkpoint: _ => throw new IOException("injected stop"));
        await Assert.ThrowsExactlyAsync<IOException>(() => interrupted.InstallAsync(second, CancellationToken.None));
        Assert.AreEqual(old.InstallDir, RegistrationPlan.InstallDirectory(Store.Read()));
        Assert.AreEqual("0.3.0-preview.2", (await CreateInstaller().InstallAsync(second, CancellationToken.None)).Version);
    }

    [TestMethod]
    public async Task RegistryFailureRestoresPreviousVersion()
    {
        string first = await PackageAsync();
        string second = await PackageAsync("0.3.0-preview.2");
        SetupReport old = await CreateInstaller().InstallAsync(first, CancellationToken.None);
        FailOnceRegistrationStore faulty = new(Store);
        Installer installer = new(Root, faulty, static (_, _) => Task.CompletedTask);
        await Assert.ThrowsExactlyAsync<IOException>(() => installer.InstallAsync(second, CancellationToken.None));
        Assert.AreEqual(old.InstallDir, RegistrationPlan.InstallDirectory(Store.Read()));
        Assert.IsFalse(File.Exists(Path.Combine(Root, "transaction.json")));
    }

    [TestMethod]
    public async Task CrashJournalRestoresOldRegistrationOnNextInstall()
    {
        string package = await PackageAsync();
        await CreateInstaller().InstallAsync(package, CancellationToken.None);
        RegistrationState old = Store.Read();
        RegistrationState after = RegistrationState.Empty();
        StateFile.Write(Path.Combine(Root, "transaction.json"), new RegistrationJournal(old, after));
        Store.Write(after);
        Assert.AreEqual("installed", (await CreateInstaller().InstallAsync(package, CancellationToken.None)).Status);
        Assert.IsTrue(RegistrationPlan.Equal(old, Store.Read()));
    }

    [TestMethod]
    public async Task ShutdownFailurePreservesInstalledRegistration()
    {
        string package = await PackageAsync();
        await CreateInstaller().InstallAsync(package, CancellationToken.None);
        RegistrationState old = Store.Read();
        Installer installer = CreateInstaller(static (_, _) => throw new SetupException("host_shutdown_timeout", "synthetic timeout"));
        await Assert.ThrowsExactlyAsync<SetupException>(() => installer.UninstallAsync(CancellationToken.None));
        Assert.IsTrue(RegistrationPlan.Equal(old, Store.Read()));
    }

    [TestMethod]
    public async Task LockedDllUnregistersAndReportsPendingThenRetries()
    {
        string package = await PackageAsync();
        Installer installer = CreateInstaller();
        SetupReport current = await installer.InstallAsync(package, CancellationToken.None);
        await using (FileStream held = new(Path.Combine(current.InstallDir!, "AssetLibrary.Explorer.dll"),
                         FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            SetupReport removed = await installer.UninstallAsync(CancellationToken.None);
            Assert.AreEqual("uninstalled_pending_cleanup", removed.Status);
            Assert.HasCount(1, removed.PendingCleanup);
            Assert.IsEmpty(Store.Read().Keys);
        }
        Assert.AreEqual("uninstalled", (await installer.UninstallAsync(CancellationToken.None)).Status);
        Assert.IsFalse(Directory.Exists(current.InstallDir));
    }

    [TestMethod]
    public async Task AddedForeignFilePreventsVersionDeletion()
    {
        string package = await PackageAsync();
        Installer installer = CreateInstaller();
        SetupReport current = await installer.InstallAsync(package, CancellationToken.None);
        string foreign = Path.Combine(current.InstallDir!, "foreign.txt");
        await File.WriteAllTextAsync(foreign, "preserve");
        await Assert.ThrowsExactlyAsync<SetupException>(() => installer.UninstallAsync(CancellationToken.None));
        Assert.AreEqual("preserve", await File.ReadAllTextAsync(foreign));
        Assert.IsNotEmpty(Store.Read().Keys);
    }

    private sealed class FailOnceRegistrationStore(IRegistrationStore inner) : IRegistrationStore
    {
        private bool failed;
        public RegistrationState Read() => inner.Read();
        public void Write(RegistrationState state)
        {
            if (!failed)
            {
                failed = true;
                inner.Write(RegistrationState.Empty());
                throw new IOException("Injected registration failure");
            }
            inner.Write(state);
        }
    }
}
