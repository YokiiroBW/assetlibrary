using System.Text.Json;

namespace AssetLibrary.Windows.Setup;

internal sealed class VersionCleanup(string root)
{
    internal async Task<string[]> RemoveVersionsAsync(CancellationToken cancellationToken)
    {
        List<string> pending = [];
        string versions = SafePath.Resolve(root, "versions");
        foreach (string directory in Directory.EnumerateDirectories(versions))
        {
            SafePath.RequireInside(versions, directory);
            try { await RemoveVersionAsync(directory, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SetupException or JsonException)
            {
                // Preserve foreign/changed/in-use content and report each exact directory for a later retry.
                pending.Add(Path.GetRelativePath(root, directory).Replace('\\', '/'));
            }
        }
        return [.. pending];
    }

    private static async Task RemoveVersionAsync(string directory, CancellationToken cancellationToken)
    {
        SafePath.RejectReparsePoints(directory);
        PackageManifest manifest = await PackageManifest.ReadAsync(Path.Combine(directory, Product.ManifestName), cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, PayloadFile> listed = manifest.Files.ToDictionary(
            file => SafePath.Resolve(directory, file.Path), StringComparer.OrdinalIgnoreCase);
        string manifestPath = Path.Combine(directory, Product.ManifestName);
        string[] existing = SafePath.Files(directory).ToArray();
        foreach (string file in existing.Where(file => !file.Equals(manifestPath, StringComparison.OrdinalIgnoreCase)))
        {
            if (!listed.TryGetValue(file, out PayloadFile? entry)) { throw new SetupException("unexpected_file", "版本目录包含其他文件。"); }
            await PackageManifest.VerifyFileAsync(file, entry, cancellationToken).ConfigureAwait(false);
        }
        foreach (string file in existing.Where(file => !file.Equals(manifestPath, StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafePath.RejectReparsePoints(file);
            File.Delete(file);
        }
        // Recursive deletion is only reached after validating every file and rejecting every reparse point.
        SafePath.RejectReparsePoints(directory);
        File.Delete(manifestPath);
        Directory.Delete(directory, true);
    }

}
