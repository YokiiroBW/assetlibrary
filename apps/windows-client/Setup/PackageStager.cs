namespace AssetLibrary.Windows.Setup;

internal sealed class PackageStager(string root, Func<long>? availableBytes)
{
    internal void CheckSpace(PackageManifest manifest)
    {
        long available = availableBytes?.Invoke() ?? new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
        if (available < manifest.Files.Sum(file => file.Size) + 64 * 1024 * 1024)
        {
            throw new SetupException("insufficient_space", "磁盘可用空间不足，尚未切换安装版本。");
        }
    }

    internal async Task StageAsync(string source, string destination, PackageManifest manifest, CancellationToken cancellationToken)
    {
        if (Directory.Exists(destination))
        {
            PackageManifest installed = await PackageManifest.ReadAsync(Path.Combine(destination, Product.ManifestName), cancellationToken)
                .ConfigureAwait(false);
            if (!ManifestEquals(installed, manifest)) { throw new SetupException("version_conflict", "同版本目录含有不同组件，拒绝覆盖。"); }
            await installed.VerifyAsync(destination, true, cancellationToken).ConfigureAwait(false);
            return;
        }
        string staging = SafePath.Resolve(root, "versions/.staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        // A manifest is written first so interruption leaves an attributable, explicitly cleanable staging directory.
        StateFile.Write(Path.Combine(staging, Product.ManifestName), manifest);
        foreach (PayloadFile file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = SafePath.Resolve(source, file.Path);
            string output = SafePath.Resolve(staging, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using FileStream from = new(input, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using FileStream to = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await from.CopyToAsync(to, cancellationToken).ConfigureAwait(false);
            await to.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        await manifest.VerifyAsync(staging, true, cancellationToken).ConfigureAwait(false);
        SafePath.RejectReparsePoints(destination);
        Directory.Move(staging, destination);
    }

    private static bool ManifestEquals(PackageManifest first, PackageManifest second) =>
        first.FormatVersion == second.FormatVersion && first.Owner == second.Owner && first.Version == second.Version &&
        first.Rid == second.Rid && first.Files.Length == second.Files.Length &&
        first.Files.OrderBy(file => file.Path, StringComparer.Ordinal).SequenceEqual(second.Files.OrderBy(file => file.Path, StringComparer.Ordinal));
}
