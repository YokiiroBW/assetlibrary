using System.Security.Cryptography;

namespace AssetLibrary.ReadCore.Tests;

internal sealed class RepositorySandbox : IDisposable
{
    private static readonly string TaskSandboxRoot = Path.Combine(
        FindRepositoryRoot(),
        ".runtime",
        "sandbox-storage",
        "V01-004",
        "tests");

    public RepositorySandbox()
    {
        Directory.CreateDirectory(TaskSandboxRoot);
        Root = Path.Combine(TaskSandboxRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CaptureStrongSnapshot()
    {
        var records = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            var attributes = File.GetAttributes(path);
            var modified = File.GetLastWriteTimeUtc(path).Ticks;
            if ((attributes & FileAttributes.Directory) != 0)
            {
                records.Add($"D|{relative}|{(int)attributes}|{modified}");
                continue;
            }

            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            records.Add($"F|{relative}|{(int)attributes}|{modified}|{stream.Length}|{hash}");
        }

        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', records))));
    }

    public void Dispose()
    {
        var resolvedRoot = Path.GetFullPath(Root);
        var resolvedTaskRoot = Path.GetFullPath(TaskSandboxRoot);
        if (!resolvedRoot.StartsWith($"{resolvedTaskRoot}{Path.DirectorySeparatorChar}", PathComparison))
        {
            throw new InvalidOperationException("Refusing to remove a directory outside the V01-004 sandbox.");
        }

        if (Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AssetLibrary.slnx"))
                && File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the AssetLibrary repository root.");
    }
}
