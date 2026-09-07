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

        var frames = new Stack<(string Directory, IEnumerator<string> Children)>();
        ValidatePhysicalDirectory(root, cancellationToken);
        frames.Push((root, OpenDirectory(root)));
        var processed = 0;
        try
        {
            while (frames.TryPeek(out var frame))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A consumer may suspend enumeration after each yield while an ancestor is replaced.
                ValidatePhysicalDirectory(frame.Directory, cancellationToken);
                if (!MoveNext(frame.Children, out var child))
                {
                    frames.Pop().Children.Dispose();
                    continue;
                }

                var entry = ReadEntry(root, child);
                if (!DefaultDiscoveryPolicy.ShouldInclude(entry))
                {
                    continue;
                }

                yield return entry;
                if (DefaultDiscoveryPolicy.ShouldRecurse(entry))
                {
                    ValidatePhysicalDirectory(child, cancellationToken);
                    frames.Push((child, OpenDirectory(child)));
                }

                processed++;
                if (processed % CooperativeYieldInterval == 0)
                {
                    await Task.Yield();
                }
            }

            ValidatePhysicalDirectory(root, cancellationToken);
        }
        finally
        {
            while (frames.TryPop(out var frame))
            {
                frame.Children.Dispose();
            }
        }
    }

    private static void ValidatePhysicalDirectory(string directory, CancellationToken cancellationToken)
    {
        var ancestor = new DirectoryInfo(directory);
        while (ancestor is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(ancestor.FullName);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new FileDiscoveryException("directory_access_denied", exception);
            }
            catch (IOException exception)
            {
                throw new FileDiscoveryException("directory_metadata_failed", exception);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new FileDiscoveryException("directory_reparse_point");
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new FileDiscoveryException("directory_unavailable");
            }

            ancestor = ancestor.Parent;
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
            var physicalRelativePath = Path.GetRelativePath(root, path);
            if (!OperatingSystem.IsWindows() && physicalRelativePath.Contains('\\'))
            {
                // The current shared path contract treats backslashes as separators; do not invent another physical location.
                throw new FileDiscoveryException("entry_path_unsupported");
            }

            var relativePath = new RelativeAssetPath(physicalRelativePath);
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
        catch (IOException exception) when (exception is not FileDiscoveryException)
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
