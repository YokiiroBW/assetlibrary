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
/// Real path boundary for one trusted root. It resolves only fully qualified paths, re-checks that the
/// result is still contained in the canonical root and refuses the root itself or any ancestor that is a
/// reparse point or symbolic link.
/// </summary>
/// <remarks>
/// Canonical comparison happens in the forward-slash form that <see cref="CanonicalLibraryRoot"/> already
/// uses, so a normal Windows directory is never refused for its separator. Real file access still uses the
/// platform-native form. This is a static check plus a re-check before and after a read; it does not claim
/// to close a hostile concurrent directory swap, which needs a strong path handle and stays behind the
/// production publication gate.
/// </remarks>
public sealed class MediaPackagePathBoundary
{
    private readonly StringComparison pathComparison;

    private MediaPackagePathBoundary(string canonicalRoot, RootPathComparison comparison)
    {
        CanonicalRoot = canonicalRoot;
        Comparison = comparison;
        pathComparison = comparison == RootPathComparison.CaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    /// <summary>
    /// The trusted root in canonical forward-slash form, exactly as the shared
    /// <see cref="CanonicalLibraryRoot"/> type spells it.
    /// </summary>
    public string CanonicalRoot { get; }

    public RootPathComparison Comparison { get; }

    /// <summary>
    /// Opens the boundary of an existing root, refusing a reparse point anywhere on its ancestor chain.
    /// The trusted root arrives in canonical form and the local path arrives in native form; both are
    /// compared in the same canonical form so a normal local directory always matches.
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
                Canonicalize(resolved),
                Canonicalize(canonical.Value),
                canonical.Comparison == RootPathComparison.CaseInsensitive
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = new MediaPackagePathBoundary(Canonicalize(canonical.Value), canonical.Comparison);
        if (!TryAnchor(resolved, out fault))
        {
            return false;
        }

        boundary = candidate;
        fault = MediaPackagePathFault.None;
        return true;
    }

    /// <summary>
    /// Resolves one POSIX package-relative path against an anchored directory and re-checks containment.
    /// The contract spelling is POSIX on every platform: a backslash is refused everywhere, while a
    /// forward slash is accepted and converted to the native separator before real access, so a legal
    /// multipart path works on Windows and on Linux alike. Traversal, an alternate separator, a device
    /// name or an escaping path is still refused by the raw-spelling policy before this point.
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
            || relativePath.Contains('\\')
            || relativePath.Contains('\0')
            || relativePath.Split('/')
                .Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            fault = MediaPackagePathFault.Unsafe;
            return false;
        }

        var native = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (!Contains(Canonicalize(anchoredDirectory), CanonicalRoot))
        {
            fault = MediaPackagePathFault.OutsideRoot;
            return false;
        }

        if (!TryResolve(Path.Combine(anchoredDirectory, native), out resolved)
            || !Contains(Canonicalize(resolved), CanonicalRoot))
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
    /// observed length when it is a regular file. A missing object and an unsafe or unreadable one are
    /// distinct faults, so a caller can never mistake a refusal for an absence.
    /// </summary>
    public bool TryObserve(
        string resolved,
        out long? length,
        out MediaPackagePathFault fault)
    {
        length = null;
        if (!Contains(Canonicalize(resolved), CanonicalRoot))
        {
            fault = MediaPackagePathFault.OutsideRoot;
            return false;
        }

        if (!TryAnchor(resolved, out fault))
        {
            return false;
        }

        try
        {
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
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            // An object that exists but cannot be inspected is unsafe, never absent.
            fault = MediaPackagePathFault.Unsafe;
            return false;
        }
    }

    /// <summary>
    /// Confirms that the path exists, is a real directory, and that neither it nor any ancestor is a link.
    /// Returns <c>null</c> when the object cannot be observed at all, which is a refusal rather than an
    /// answer: a caller must never read "not a directory" out of an observation that did not happen. A
    /// regular file answers <c>false</c> with <see cref="MediaPackagePathFault.None"/>, because it was
    /// observed successfully and it is simply not a directory.
    /// </summary>
    public bool? TryObserveDirectory(string resolved, out MediaPackagePathFault fault)
    {
        if (!Contains(Canonicalize(resolved), CanonicalRoot))
        {
            fault = MediaPackagePathFault.OutsideRoot;
            return null;
        }

        if (!TryAnchor(resolved, out fault))
        {
            return null;
        }

        try
        {
            // Directory.Exists reports false both for "there is a file here" and for "nothing is here",
            // so the two are separated before it is consulted and an unobservable object is never read as
            // an absence.
            if (File.Exists(resolved))
            {
                fault = MediaPackagePathFault.None;
                return false;
            }

            if (Directory.Exists(resolved))
            {
                fault = MediaPackagePathFault.None;
                return true;
            }

            fault = MediaPackagePathFault.Missing;
            return null;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            // An object that exists but cannot be inspected is unsafe, never absent.
            fault = MediaPackagePathFault.Unsafe;
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="root"/> or lies inside it. Both values
    /// are compared in canonical form, and a volume root keeps its trailing separator so it can contain
    /// its own descendants.
    /// </summary>
    public bool Contains(string candidate, string root)
    {
        var normalizedCandidate = Canonicalize(candidate);
        var normalizedRoot = Canonicalize(root);
        if (string.Equals(normalizedCandidate, normalizedRoot, pathComparison))
        {
            return true;
        }

        if (normalizedCandidate.Length <= normalizedRoot.Length
            || !normalizedCandidate.StartsWith(normalizedRoot, pathComparison))
        {
            return false;
        }

        return normalizedRoot[^1] == '/' || normalizedCandidate[normalizedRoot.Length] == '/';
    }

    /// <summary>
    /// True when the two paths are equal or one contains the other, in either direction.
    /// </summary>
    public bool Overlaps(string left, string right) =>
        Contains(left, right) || Contains(right, left);

    /// <summary>
    /// Walks one directory tree under a trusted root and returns every file and directory it really
    /// contains as package-relative POSIX paths. Entries are counted and cancellation is observed while
    /// the listing is consumed, so the work and the memory stay bounded by the instance budget rather
    /// than by the size of a directory.
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
            foreach (var child in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scanned > MediaPackageInspectionLimits.MaximumEnumeratedEntries)
                {
                    issues.Record("budget_exceeded");
                    return new PackageListing(files, directories, scanned);
                }

                var name = Path.GetFileName(child);
                var relative = prefix.Length == 0 ? name : prefix + "/" + name;
                if (!TryResolveChild(directory, name, out var resolved, out var fault))
                {
                    issues.Record(FaultCode(fault), relative);
                    return new PackageListing(files, directories, scanned);
                }

                if (IsReparsePoint(resolved))
                {
                    issues.Record("unsafe_path", relative);
                    return new PackageListing(files, directories, scanned);
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
                    return new PackageListing(files, directories, scanned);
                }

                files.Add(relative);
            }
        }

        return new PackageListing(files, directories, scanned);
    }

    /// <summary>
    /// Length and last-write stamp of one existing file, taken before and after a read so a substitution
    /// or a growth during the run becomes a verdict.
    /// </summary>
    public static (long Length, DateTimeOffset ModifiedAt) Stamp(string absolutePath)
    {
        var info = new FileInfo(absolutePath);
        return (info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>
    /// Maps a path fault onto the frozen report vocabulary. <see cref="MediaPackagePathFault.None"/> is
    /// never a failure, so a caller that reaches here with it has an internal fault, not an IO one.
    /// </summary>
    public static string FaultCode(MediaPackagePathFault fault) => fault switch
    {
        MediaPackagePathFault.Missing => "source_missing",
        MediaPackagePathFault.OutsideRoot => "unsafe_path",
        MediaPackagePathFault.Unsafe => "unsafe_path",
        _ => "invalid_file_set",
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
    /// The real contents of one package root, as package-relative POSIX paths, with the number of entries
    /// actually consumed while walking.
    /// </summary>
    public sealed record PackageListing(
        IReadOnlyList<string> Files,
        IReadOnlyList<string> Directories,
        int EnumeratedEntries);

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
            resolved = TrimNativeSeparators(Path.GetFullPath(path));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Walks the path and refuses any component that exists and is a reparse point. A component that
    /// does not exist ends the walk: a missing segment cannot be a link, and a later component cannot be
    /// reached through it.
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

    /// <summary>
    /// Canonical forward-slash form used for every comparison. A volume root and the POSIX root keep
    /// their trailing separator so containment of their own descendants stays provable.
    /// </summary>
    private static string Canonicalize(string value)
    {
        var slashed = value.Replace('\\', '/');
        var root = Path.GetPathRoot(slashed)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(root) && string.Equals(slashed, root, StringComparison.Ordinal))
        {
            return slashed;
        }

        var trimmed = slashed.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static string TrimNativeSeparators(string value)
    {
        var root = Path.GetPathRoot(value);
        if (string.IsNullOrEmpty(root) || string.Equals(value, root, StringComparison.Ordinal))
        {
            return value;
        }

        return value.TrimEnd(Path.DirectorySeparatorChar);
    }
}
