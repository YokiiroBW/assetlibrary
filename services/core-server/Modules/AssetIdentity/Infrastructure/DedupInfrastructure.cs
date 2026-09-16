using System.Runtime.CompilerServices;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Infrastructure;

/// <summary>
/// Read-only enumeration for the preview. It walks the allowed root itself instead of borrowing the
/// scan module's internal discovery port: a cross-module dependency on another module's internal
/// layers is forbidden, and it would also make the two modules depend on each other.
/// </summary>
public sealed class SystemDedupFileDiscovery : IDedupFileDiscovery
{
    private const int CooperativeYieldInterval = 128;

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN",
        ".assetmeta",
        ".cache",
        ".Trashes",
        "@eaDir",
        "System Volume Information",
    };

    private static readonly HashSet<string> SkippedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
        "desktop.ini",
        "Thumbs.db",
    };

    public async IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
        CanonicalLibraryRoot root,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var physicalRoot = Path.GetFullPath(root.Value);
        if (!Directory.Exists(physicalRoot))
        {
            throw new DedupDiscoveryException("storage_unavailable");
        }

        // A reparse point anywhere above the root would make the walk leave the allowed directory,
        // so the whole ancestry is checked before a single entry is read.
        if (HasReparsePointAncestor(physicalRoot))
        {
            throw new DedupDiscoveryException("directory_reparse_point");
        }

        var frames = new Stack<(string Directory, IEnumerator<string> Children)>();
        frames.Push((physicalRoot, OpenDirectory(physicalRoot)));
        var processed = 0;
        try
        {
            while (frames.TryPeek(out var frame))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MoveNext(frame.Children, out var child))
                {
                    frames.Pop().Children.Dispose();
                    continue;
                }

                var isDirectory = Directory.Exists(child);
                var isReparsePoint = IsReparsePoint(child);
                var relative = new RelativeAssetPath(
                    Path.GetRelativePath(physicalRoot, child).Replace('\\', '/'));
                var kind = (isDirectory, isReparsePoint) switch
                {
                    (true, true) => AssetEntryKind.ReparseDirectory,
                    (true, false) => AssetEntryKind.Directory,
                    (false, true) => AssetEntryKind.ReparseFile,
                    _ => AssetEntryKind.File,
                };
                var excluded = IsExcluded(kind, relative.Name);

                // A plain directory is only a step in the walk: the port reports files, so a
                // directory is never offered as an asset. A reparse-point directory is reported
                // because it is its own entity that the preview must name and never follow into.
                if (!isDirectory || isReparsePoint)
                {
                    yield return new DiscoveredFile(
                        relative,
                        isReparsePoint,
                        excluded,
                        isDirectory ? 0 : ReadLength(child),
                        ReadLastWriteTime(child));
                }

                // A reparse point is never walked into, so a junction or symlink cannot make one
                // library appear inside another.
                if (isDirectory && !isReparsePoint && !excluded)
                {
                    frames.Push((child, OpenDirectory(child)));
                }

                processed++;
                if (processed % CooperativeYieldInterval == 0)
                {
                    await Task.Yield();
                }
            }
        }
        finally
        {
            while (frames.TryPop(out var frame))
            {
                frame.Children.Dispose();
            }
        }
    }

    private static bool MoveNext(IEnumerator<string> children, out string child)
    {
        try
        {
            if (children.MoveNext())
            {
                child = children.Current;
                return true;
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new DedupDiscoveryException("directory_access_denied", exception);
        }
        catch (IOException exception)
        {
            throw new DedupDiscoveryException("directory_metadata_failed", exception);
        }

        child = string.Empty;
        return false;
    }

    private static IEnumerator<string> OpenDirectory(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new DedupDiscoveryException("directory_access_denied", exception);
        }
        catch (IOException exception)
        {
            throw new DedupDiscoveryException("directory_metadata_failed", exception);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new DedupDiscoveryException("entry_access_denied", exception);
        }
        catch (IOException exception)
        {
            throw new DedupDiscoveryException("entry_metadata_failed", exception);
        }
    }

    /// <summary>
    /// True when the path itself or any directory above it is a reparse point. A junction in the
    /// ancestry would silently redirect the whole walk outside the allowed root.
    /// </summary>
    private static bool HasReparsePointAncestor(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (IsReparsePoint(current.FullName))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static long ReadLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new DedupDiscoveryException("entry_access_denied", exception);
        }
        catch (IOException exception)
        {
            throw new DedupDiscoveryException("entry_metadata_failed", exception);
        }
    }

    private static DateTimeOffset ReadLastWriteTime(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new DedupDiscoveryException("entry_access_denied", exception);
        }
        catch (IOException exception)
        {
            throw new DedupDiscoveryException("entry_metadata_failed", exception);
        }
    }

    /// <summary>
    /// The same exclusions the shared read-only walk applies, so a temp file, a partial download or
    /// a thumbnail cache is never offered as an asset by this preview either.
    /// </summary>
    private static bool IsExcluded(AssetEntryKind kind, string name)
    {
        if ((kind & (AssetEntryKind.Directory | AssetEntryKind.ReparseDirectory)) != 0)
        {
            return SkippedDirectoryNames.Contains(name)
                || name.StartsWith(".Trash-", StringComparison.OrdinalIgnoreCase);
        }

        return SkippedFileNames.Contains(name)
            || name.StartsWith("~$", StringComparison.Ordinal)
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
    }
}



/// <summary>
/// Probes whether a source can be reached at all. A missing or unreachable directory is reported as
/// offline instead of being treated as an empty source. A reparse point is deliberately left to the
/// walk, which owns the ancestry check and names the refusal precisely rather than reporting a
/// handleable directory as unavailable.
/// </summary>
public sealed class SystemDedupSourceAvailability : IDedupSourceAvailability
{
    public ValueTask<StorageAvailability> CheckAsync(
        CanonicalLibraryRoot root,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return ValueTask.FromResult(
                Directory.Exists(root.Value) ? StorageAvailability.Online : StorageAvailability.Offline);
        }
        catch (UnauthorizedAccessException)
        {
            return ValueTask.FromResult(StorageAvailability.Offline);
        }
        catch (IOException)
        {
            return ValueTask.FromResult(StorageAvailability.Offline);
        }
    }
}
