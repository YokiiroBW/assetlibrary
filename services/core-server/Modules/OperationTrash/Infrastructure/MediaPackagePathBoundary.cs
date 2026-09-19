using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Infrastructure;

/// <summary>
/// Failure of a path anchor or containment check. Every member maps to one frozen report code, so the
/// inspector never translates a framework exception into a package verdict.
/// </summary>
public enum MediaPackagePathFault
{
    None = 0,
    Unsafe = 1,
    Missing = 2,
    OutsideRoot = 3,
}

/// <summary>
/// Real path boundary for one trusted root. It resolves only fully qualified, already normalized paths
/// without alternate separators, re-checks that the result is still contained in the canonical root,
/// and refuses the root itself or any ancestor that is a reparse point or symbolic link.
/// </summary>
/// <remarks>
/// This is a static check plus a re-check before and after a read. It does not claim to close a hostile
/// concurrent directory swap; that needs a strong path handle and stays behind the production
/// publication gate.
/// </remarks>
public sealed class MediaPackagePathBoundary
{
    private readonly StringComparison pathComparison;

    private MediaPackagePathBoundary(string canonicalRoot, RootPathComparison comparison)
    {
        CanonicalRoot = canonicalRoot;
        Comparison = comparison;
        pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public string CanonicalRoot { get; }

    public RootPathComparison Comparison { get; }

    /// <summary>
    /// Opens the boundary of an existing root, refusing a reparse point anywhere on its ancestor chain.
    /// </summary>
    public static bool TryOpen(
        string root,
        CanonicalLibraryRoot canonical,
        out MediaPackagePathBoundary? boundary,
        out MediaPackagePathFault fault)
    {
        boundary = null;
        fault = MediaPackagePathFault.Unsafe;
        if (!TryResolve(root, out var resolved)
            || !string.Equals(
                TrimSeparators(resolved),
                TrimSeparators(canonical.Value),
                StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = new MediaPackagePathBoundary(canonical.Value, canonical.Comparison);
        if (!TryAnchor(resolved, out fault))
        {
            return false;
        }

        boundary = candidate;
        fault = MediaPackagePathFault.None;
        return true;
    }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> against an anchored directory and re-checks
    /// containment. The relative path must use platform separators, like a package-relative path
    /// mapped from the POSIX manifest form.
    /// </summary>
    public bool TryResolveChild(
        string anchoredDirectory,
        string relativePath,
        out string resolved,
        out MediaPackagePathFault fault)
    {
        resolved = string.Empty;
        if (string.IsNullOrEmpty(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(Path.AltDirectorySeparatorChar)
            || relativePath.Contains('\0')
            || relativePath.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            fault = MediaPackagePathFault.Unsafe;
            return false;
        }

        if (!Contains(anchoredDirectory, CanonicalRoot))
        {
            fault = MediaPackagePathFault.OutsideRoot;
            return false;
        }

        if (!TryResolve(Path.Combine(anchoredDirectory, relativePath), out resolved)
            || !Contains(resolved, CanonicalRoot))
        {
            resolved = string.Empty;
            fault = MediaPackagePathFault.OutsideRoot;
            return false;
        }

        fault = MediaPackagePathFault.None;
        return true;
    }

    /// <summary>
    /// Confirms that the path still exists and that neither it nor any ancestor is a link. Returns the
    /// observed length when it is a regular file.
    /// </summary>
    public bool TryObserve(
        string resolved,
        out long? length,
        out MediaPackagePathFault fault)
    {
        length = null;
        if (!Contains(resolved, CanonicalRoot))
        {
            fault = MediaPackagePathFault.OutsideRoot;
            return false;
        }

        if (!TryAnchor(resolved, out fault))
        {
            return false;
        }

        if (Directory.Exists(resolved))
        {
            fault = MediaPackagePathFault.None;
            return true;
        }

        if (!File.Exists(resolved))
        {
            fault = MediaPackagePathFault.Missing;
            return false;
        }

        length = new FileInfo(resolved).Length;
        fault = MediaPackagePathFault.None;
        return true;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="root"/> or lies inside it.
    /// </summary>
    public bool Contains(string candidate, string root)
    {
        var normalizedCandidate = TrimSeparators(Normalize(candidate));
        var normalizedRoot = TrimSeparators(Normalize(root));
        if (string.Equals(normalizedCandidate, normalizedRoot, pathComparison))
        {
            return true;
        }

        return normalizedCandidate.Length > normalizedRoot.Length
            && normalizedCandidate.StartsWith(normalizedRoot, pathComparison)
            && IsSeparator(normalizedCandidate[normalizedRoot.Length]);
    }

    /// <summary>
    /// True when the two paths are equal or one contains the other, in either direction.
    /// </summary>
    public bool Overlaps(string left, string right) =>
        Contains(left, right) || Contains(right, left);

    /// <summary>
    /// Walks one directory tree under a trusted root and returns every file and directory it really
    /// contains as package-relative POSIX paths. It refuses a reparse point, an entry that is neither a
    /// file nor a directory, a path that leaves the root, and a tree larger than the instance budget, so
    /// the caller always compares the declaration against the real set rather than a filtered view.
    /// </summary>
    public PackageListing Enumerate(
        string root,
        MediaPackageIssueSink issues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var files = new List<string>();
        var directories = new List<string>();
        var pending = new Queue<(string Directory, string Prefix)>();
        pending.Enqueue((root, string.Empty));
        var scanned = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, prefix) = pending.Dequeue();
            var children = Directory.EnumerateFileSystemEntries(directory).ToArray();
            Array.Sort(children, StringComparer.Ordinal);
            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scanned > MediaPackageInspectionLimits.MaximumEnumeratedEntries)
                {
                    issues.Record("budget_exceeded");
                    return new PackageListing(files, directories);
                }

                var name = Path.GetFileName(child);
                var relative = prefix.Length == 0 ? name : prefix + "/" + name;
                if (!TryResolveChild(directory, name, out var resolved, out var fault))
                {
                    issues.Record(FaultCode(fault), relative);
                    return new PackageListing(files, directories);
                }

                if (IsReparsePoint(resolved))
                {
                    issues.Record("unsafe_path", relative);
                    return new PackageListing(files, directories);
                }

                if (Directory.Exists(resolved))
                {
                    directories.Add(relative);
                    pending.Enqueue((resolved, relative));
                    continue;
                }

                if (!File.Exists(resolved))
                {
                    issues.Record("invalid_file_set", relative);
                    return new PackageListing(files, directories);
                }

                files.Add(relative);
            }
        }

        return new PackageListing(files, directories);
    }

    /// <summary>
    /// Length and last-write stamp of one existing file, taken before and after a read so a quiet
    /// substitution during the read becomes a verdict.
    /// </summary>
    public static (long Length, DateTimeOffset ModifiedAt) Stamp(string absolutePath)
    {
        var info = new FileInfo(absolutePath);
        return (info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>
    /// Maps a path fault onto the frozen report vocabulary.
    /// </summary>
    public static string FaultCode(MediaPackagePathFault fault) => fault switch
    {
        MediaPackagePathFault.Missing => "source_missing",
        MediaPackagePathFault.OutsideRoot => "unsafe_path",
        MediaPackagePathFault.Unsafe => "unsafe_path",
        _ => "io_failure",
    };

    /// <summary>
    /// Picks the code for a two-step resolve-and-observe failure, where the observe fault is the more
    /// specific one when it exists.
    /// </summary>
    public static string FaultReason(
        MediaPackagePathFault resolveFault,
        MediaPackagePathFault observeFault,
        string missingCode = "unsafe_path") => observeFault switch
    {
        MediaPackagePathFault.Missing => "source_missing",
        MediaPackagePathFault.None => resolveFault == MediaPackagePathFault.None
            ? missingCode
            : FaultCode(resolveFault),
        _ => FaultCode(observeFault),
    };

    private static bool IsReparsePoint(string resolved) =>
        File.GetAttributes(resolved).HasFlag(FileAttributes.ReparsePoint);

    /// <summary>
    /// The real contents of one package root, as package-relative POSIX paths.
    /// </summary>
    public sealed record PackageListing(
        IReadOnlyList<string> Files,
        IReadOnlyList<string> Directories);

    private static bool TryResolve(string path, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('\0')
            || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            resolved = TrimSeparators(Normalize(Path.GetFullPath(path)));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Walks the path and refuses any component that exists and is a reparse point. A component that
    /// does not exist ends the walk: a missing segment cannot be a link, and a later component cannot
    /// be reached through it.
    /// </summary>
    private static bool TryAnchor(string resolved, out MediaPackagePathFault fault)
    {
        fault = MediaPackagePathFault.None;
        if (!TryResolve(resolved, out var normalized))
        {
            fault = MediaPackagePathFault.Unsafe;
            return false;
        }

        try
        {
            foreach (var component in EnumerateComponents(normalized))
            {
                if (!File.Exists(component) && !Directory.Exists(component))
                {
                    return true;
                }

                if (File.GetAttributes(component).HasFlag(FileAttributes.ReparsePoint))
                {
                    fault = MediaPackagePathFault.Unsafe;
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            fault = MediaPackagePathFault.Unsafe;
            return false;
        }
    }

    /// <summary>
    /// Every ancestor of the path, from the volume root down to the path itself, so a link anywhere on
    /// the chain is caught.
    /// </summary>
    private static IEnumerable<string> EnumerateComponents(string normalized)
    {
        var volumeRoot = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(volumeRoot))
        {
            yield break;
        }

        var current = volumeRoot;
        yield return current;
        foreach (var segment in normalized[volumeRoot.Length..].Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            yield return current;
        }
    }

    private static string Normalize(string value) =>
        value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string TrimSeparators(string value)
    {
        var root = Path.GetPathRoot(value);
        if (string.IsNullOrEmpty(root) || string.Equals(value, root, StringComparison.Ordinal))
        {
            return value;
        }

        return value.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool IsSeparator(char value) =>
        value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
}
