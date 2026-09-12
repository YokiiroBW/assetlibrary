using System.Security.Cryptography;
using System.Text.Json;

namespace AssetLibrary.Windows.Setup;

internal sealed record PayloadFile(string Path, long Size, string Sha256);
internal sealed record PackageManifest(int FormatVersion, string Owner, string Version, string Rid, PayloadFile[] Files)
{
    private const long MaximumBytes = 2L * 1024 * 1024 * 1024;
    private static readonly string[] RequiredFiles = ["AssetLibrary.Setup.exe", "AssetLibrary.Settings.exe",
        "AssetLibrary.Host.exe", "AssetLibrary.Explorer.dll"];

    internal static async Task<PackageManifest> ReadAsync(string path, CancellationToken cancellationToken)
    {
        SafePath.RejectReparsePoints(path);
        if (new FileInfo(path).Length > 2 * 1024 * 1024)
        {
            throw new SetupException("manifest_limit", "安装清单超过大小限制。");
        }
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        PackageManifest manifest = await JsonSerializer.DeserializeAsync<PackageManifest>(input, Product.Json, cancellationToken)
            .ConfigureAwait(false) ?? throw new SetupException("invalid_manifest", "安装清单为空。");
        manifest.Validate();
        return manifest;
    }

    internal void Validate()
    {
        if (FormatVersion != 1 || Owner != Product.Owner || Rid != "win-x64" ||
            string.IsNullOrWhiteSpace(Version) || Version.Length > 64 ||
            !Version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
        {
            throw new SetupException("invalid_manifest", "安装清单版本或产品标识不受支持。");
        }
        if (Files is null || Files.Length is < 4 or > 4096)
        {
            throw new SetupException("manifest_limit", "安装清单文件数量不受支持。");
        }
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (PayloadFile file in Files)
        {
            ValidateEntry(file);
            if (!paths.Add(file.Path))
            {
                throw new SetupException("invalid_manifest", "安装清单包含重复路径或无效校验值。");
            }
            if (file.Path.Equals(Product.ManifestName, StringComparison.OrdinalIgnoreCase) ||
                file.Path.StartsWith('.'))
            {
                throw new SetupException("reserved_path", "安装清单占用了安装器保留路径。");
            }
            total = checked(total + file.Size);
        }
        if (total > MaximumBytes || !RequiredFiles.All(paths.Contains))
        {
            throw new SetupException("incomplete_payload", "安装包缺少必需组件或超过大小限制。");
        }
    }

    private static void ValidateEntry(PayloadFile file)
    {
        if (file is null || string.IsNullOrEmpty(file.Path) || file.Size is < 0 or > MaximumBytes ||
            string.IsNullOrEmpty(file.Sha256) || file.Sha256.Length != 64 || !file.Sha256.All(char.IsAsciiHexDigit))
        {
            throw new SetupException("invalid_manifest", "安装清单包含无效文件项。");
        }
    }

    internal async Task VerifyAsync(string root, bool installed, CancellationToken cancellationToken)
    {
        HashSet<string> expected = new(StringComparer.OrdinalIgnoreCase);
        foreach (PayloadFile file in Files)
        {
            string path = SafePath.Resolve(root, file.Path);
            expected.Add(path);
            await VerifyFileAsync(path, file, cancellationToken).ConfigureAwait(false);
        }
        if (installed) { expected.Add(Path.Combine(root, Product.ManifestName)); }
        if (SafePath.Files(root).Any(path => !expected.Contains(path)))
        {
            throw new SetupException("unexpected_file", "目录包含清单以外的文件，拒绝覆盖或删除。");
        }
    }

    internal static async Task VerifyFileAsync(string path, PayloadFile file, CancellationToken cancellationToken)
    {
        SafePath.RejectReparsePoints(path);
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != file.Size || !Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)
                .ConfigureAwait(false)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new SetupException("hash_mismatch", "安装组件大小或 SHA256 校验不符。");
        }
    }
}
