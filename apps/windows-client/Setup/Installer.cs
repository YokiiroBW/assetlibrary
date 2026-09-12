using System.Text.Json;

namespace AssetLibrary.Windows.Setup;

internal sealed class Installer(
    string root,
    IRegistrationStore registration,
    Func<string, CancellationToken, Task> stopHost,
    Func<long>? availableBytes = null,
    Action<string>? checkpoint = null)
{
    internal async Task<SetupReport> InstallAsync(string package, CancellationToken cancellationToken)
    {
        string source = Path.Combine(Path.GetFullPath(package), "payload");
        PackageManifest manifest = await PackageManifest.ReadAsync(Path.Combine(package, Product.ManifestName), cancellationToken)
            .ConfigureAwait(false);
        await manifest.VerifyAsync(source, false, cancellationToken).ConfigureAwait(false);
        await PackageManifest.VerifyFileAsync(Path.Combine(package, "AssetLibrary.Setup.exe"),
            manifest.Files.Single(file => file.Path.Equals("AssetLibrary.Setup.exe", StringComparison.OrdinalIgnoreCase)), cancellationToken).ConfigureAwait(false);
        EnsureRoot();
        using FileStream operationLock = AcquireLock();
        RegistrationTransaction transaction = new(root, registration);
        transaction.Recover();
        RegistrationState previous = registration.Read();
        RegistrationPlan.ValidateOwnership(previous);
        string destination = SafePath.Resolve(root, "versions/" + manifest.Version);
        PackageStager staging = new(root, availableBytes);
        staging.CheckSpace(manifest);
        await staging.StageAsync(source, destination, manifest, cancellationToken).ConfigureAwait(false);
        checkpoint?.Invoke("staged");
        string? oldDirectory = RegistrationPlan.InstallDirectory(previous);
        if (oldDirectory is not null) { await StopVerifiedHostAsync(oldDirectory, cancellationToken).ConfigureAwait(false); }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit(previous, RegistrationPlan.Create(destination, manifest.Version));
        return new("installed", manifest.Version, destination, ReadPending());
    }

    internal async Task<SetupReport> UninstallAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            if (registration.Read().Keys.Count != 0) { throw new SetupException("missing_installation", "注册项存在但安装目录缺失，未修改注册。"); }
            return new("not_installed", null, null, []);
        }
        EnsureRoot();
        using FileStream operationLock = AcquireLock();
        RegistrationTransaction transaction = new(root, registration);
        transaction.Recover();
        RegistrationState previous = registration.Read();
        RegistrationPlan.ValidateOwnership(previous);
        string? current = RegistrationPlan.InstallDirectory(previous);
        if (current is not null) { await StopVerifiedHostAsync(current, cancellationToken).ConfigureAwait(false); }
        transaction.Commit(previous, RegistrationState.Empty());
        string[] pending = await new VersionCleanup(root).RemoveVersionsAsync(cancellationToken).ConfigureAwait(false);
        StateFile.Write(Path.Combine(root, "pending-cleanup.json"), pending);
        return new(pending.Length == 0 ? "uninstalled" : "uninstalled_pending_cleanup", null, null, pending);
    }

    internal SetupReport Status()
    {
        if (Directory.Exists(root)) { ValidateRootOwner(); }
        RegistrationState current = registration.Read();
        RegistrationPlan.ValidateOwnership(current);
        string? directory = RegistrationPlan.InstallDirectory(current);
        string? version = directory is null ? null : Path.GetFileName(directory);
        string[] pending = ReadPending();
        string status = directory is not null ? "registered_current_process_view" :
            pending.Length > 0 ? "uninstalled_pending_cleanup" : "not_installed";
        return new(status, version, directory, pending);
    }

    private void EnsureRoot()
    {
        SafePath.RejectReparsePoints(root);
        if (Directory.Exists(root)) { ValidateRootOwner(); }
        else
        {
            Directory.CreateDirectory(root);
            StateFile.Write(Path.Combine(root, Product.OwnerFile), new Ownership(Product.Owner));
        }
        string versions = SafePath.Resolve(root, "versions");
        Directory.CreateDirectory(versions);
    }

    private void ValidateRootOwner()
    {
        string marker = Path.Combine(root, Product.OwnerFile);
        SafePath.RejectReparsePoints(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length > 1024 ||
            JsonSerializer.Deserialize<Ownership>(File.ReadAllText(marker), Product.Json)?.Owner != Product.Owner)
        {
            throw new SetupException("foreign_owner", "安装目录不属于资产库，拒绝修改。");
        }
    }

    private FileStream AcquireLock()
    {
        string path = SafePath.Resolve(root, ".setup.lock");
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private async Task StopVerifiedHostAsync(string directory, CancellationToken cancellationToken)
    {
        SafePath.RequireInside(Path.Combine(root, "versions"), directory);
        PackageManifest old = await PackageManifest.ReadAsync(Path.Combine(directory, Product.ManifestName), cancellationToken)
            .ConfigureAwait(false);
        await old.VerifyAsync(directory, true, cancellationToken).ConfigureAwait(false);
        await stopHost(directory, cancellationToken).ConfigureAwait(false);
    }

    private string[] ReadPending()
    {
        string path = Path.Combine(root, "pending-cleanup.json");
        SafePath.RejectReparsePoints(path);
        if (!File.Exists(path)) { return []; }
        if (new FileInfo(path).Length > 64 * 1024) { throw new SetupException("invalid_state", "清理记录超过大小限制。"); }
        return JsonSerializer.Deserialize<string[]>(File.ReadAllText(path), Product.Json) ?? [];
    }

}
