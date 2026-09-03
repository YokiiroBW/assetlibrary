namespace AssetLibrary.TransferOperation.Tests;

internal sealed class SandboxPathBoundary
{
    public const string MarkerName = ".assetlibrary-v01-007-sandbox";
    public const string MarkerValue = "assetlibrary-v01-007-sandbox-v1";
    private const string InternalDirectory = ".v01-007";
    private readonly StringComparison pathComparison;

    private SandboxPathBoundary(string repositoryRoot, string allowedBase, string root)
    {
        RepositoryRoot = repositoryRoot;
        AllowedBase = allowedBase;
        Root = root;
        pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public string RepositoryRoot { get; }

    public string AllowedBase { get; }

    public string Root { get; }

    public static SandboxPathBoundary Open(string repositoryRoot, string fixtureRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureRoot);
        if (!Path.IsPathFullyQualified(repositoryRoot)
            || !Path.IsPathFullyQualified(fixtureRoot))
        {
            throw new InvalidOperationException("Sandbox roots must be fully qualified.");
        }

        var repository = Path.GetFullPath(repositoryRoot);
        var allowed = Path.GetFullPath(
            Path.Combine(repository, ".runtime", "sandbox-storage", "V01-007"));
        var root = Path.GetFullPath(fixtureRoot);
        var candidate = new SandboxPathBoundary(repository, allowed, root);
        if (!candidate.IsStrictChild(root, allowed)
            || !candidate.IsStrictChild(allowed, repository))
        {
            throw new InvalidOperationException(
                "The fixture must be a strict child of the V01-007 sandbox root.");
        }

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("The sandbox fixture root does not exist.");
        }

        EnsureNoReparse(repository, allowed);
        EnsureNoReparse(allowed, root);
        var marker = Path.Combine(root, MarkerName);
        if (!File.Exists(marker)
            || !string.Equals(
                File.ReadAllText(marker),
                MarkerValue,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The V01-007 sandbox marker is missing or invalid.");
        }

        return candidate;
    }

    public string ResolveRelative(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Contains('\0')
            || relativePath.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part is "." or ".."))
        {
            throw new InvalidOperationException("Sandbox paths must be normalized relative paths.");
        }

        var resolved = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!IsStrictChild(resolved, Root))
        {
            throw new InvalidOperationException("The sandbox path escaped its fixture root.");
        }

        EnsureNoReparse(Root, resolved);
        return resolved;
    }

    public string ResolveInternal(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Length == 0
            || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment.Contains(Path.DirectorySeparatorChar)
                || segment.Contains(Path.AltDirectorySeparatorChar)
                || segment is "." or ".."))
        {
            throw new InvalidOperationException("Internal sandbox path segments are invalid.");
        }

        return ResolveRelative(
            Path.Combine([InternalDirectory, .. segments]));
    }

    public string ToRelative(string path)
    {
        var resolved = Path.GetFullPath(path);
        if (!IsStrictChild(resolved, Root))
        {
            throw new InvalidOperationException("The path is outside the sandbox fixture.");
        }

        return Path.GetRelativePath(Root, resolved);
    }

    public void Revalidate(string path)
    {
        var resolved = Path.GetFullPath(path);
        if (!IsStrictChild(resolved, Root))
        {
            throw new InvalidOperationException("The path is outside the sandbox fixture.");
        }

        EnsureNoReparse(Root, resolved);
    }

    private bool IsStrictChild(string child, string parent)
    {
        var normalizedParent = parent.EndsWith(Path.DirectorySeparatorChar)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return child.StartsWith(normalizedParent, pathComparison)
            && !string.Equals(child, parent, pathComparison);
    }

    private static void EnsureNoReparse(string ancestor, string descendant)
    {
        if (!Directory.Exists(ancestor)
            || File.GetAttributes(ancestor).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("A sandbox ancestor is missing or is a reparse point.");
        }

        var relative = Path.GetRelativePath(ancestor, descendant);
        var current = ancestor;
        foreach (var part in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }

            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException("Reparse points are forbidden in a sandbox path.");
            }
        }
    }
}
