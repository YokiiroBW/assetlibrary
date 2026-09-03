using System.Runtime.CompilerServices;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Domain;

namespace AssetLibrary.Modules.ScanReconciliation.Infrastructure;

public sealed class SystemReadOnlyFileDiscovery : IReadOnlyFileDiscovery
{
    private const int CooperativeYieldInterval = 128;

    public async IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(
        LibraryScanTarget target,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Availability != StorageAvailability.Online)
        {
            throw new FileDiscoveryException("storage_offline");
        }

        var root = Path.GetFullPath(target.Root.Value);
        if (!Directory.Exists(root))
        {
            throw new FileDiscoveryException("storage_unavailable");
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);
        var processed = 0;
        while (pendingDirectories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var children = OpenDirectory(directory);
            while (MoveNext(children, out var child))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = ReadEntry(root, child);
                if (!DefaultDiscoveryPolicy.ShouldInclude(entry))
                {
                    continue;
                }

                yield return entry;
                if (DefaultDiscoveryPolicy.ShouldRecurse(entry))
                {
                    pendingDirectories.Push(child);
                }

                processed++;
                if (processed % CooperativeYieldInterval == 0)
                {
                    await Task.Yield();
                }
            }
        }
    }

    private static IEnumerator<string> OpenDirectory(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new FileDiscoveryException("directory_access_denied", exception);
        }
        catch (IOException exception)
        {
            throw new FileDiscoveryException("directory_enumeration_failed", exception);
        }
    }

    private static bool MoveNext(IEnumerator<string> children, out string child)
    {
        try
        {
            if (!children.MoveNext())
            {
                child = string.Empty;
                return false;
            }

            child = children.Current;
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new FileDiscoveryException("directory_access_denied", exception);
        }
        catch (IOException exception)
        {
            throw new FileDiscoveryException("directory_enumeration_failed", exception);
        }
    }

    private static DiscoveredEntry ReadEntry(string root, string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
            var kind = SelectKind(isDirectory, isReparsePoint);
            var relativePath = new RelativeAssetPath(Path.GetRelativePath(root, path));
            var contentLength = isDirectory || isReparsePoint ? (long?)null : new FileInfo(path).Length;
            var observedAttributes = ToDiscoveryAttributes(attributes);
            return new DiscoveredEntry(
                relativePath,
                kind,
                contentLength,
                File.GetLastWriteTimeUtc(path),
                observedAttributes);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new FileDiscoveryException("entry_access_denied", exception);
        }
        catch (IOException exception)
        {
            throw new FileDiscoveryException("entry_metadata_failed", exception);
        }
    }

    private static AssetEntryKind SelectKind(bool isDirectory, bool isReparsePoint) =>
        (isDirectory, isReparsePoint) switch
        {
            (true, true) => AssetEntryKind.ReparseDirectory,
            (true, false) => AssetEntryKind.Directory,
            (false, true) => AssetEntryKind.ReparseFile,
            _ => AssetEntryKind.File,
        };

    private static DiscoveryAttributes ToDiscoveryAttributes(FileAttributes attributes)
    {
        var result = DiscoveryAttributes.None;
        if ((attributes & FileAttributes.Hidden) != 0)
        {
            result |= DiscoveryAttributes.Hidden;
        }

        if ((attributes & FileAttributes.System) != 0)
        {
            result |= DiscoveryAttributes.System;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            result |= DiscoveryAttributes.ReparsePoint;
        }

        return result;
    }
}
