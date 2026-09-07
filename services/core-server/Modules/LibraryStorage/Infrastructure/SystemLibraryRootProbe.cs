using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.LibraryStorage.Infrastructure;

public sealed class SystemLibraryRootProbe : ILibraryRootProbe
{
    public ValueTask<LibraryRootProbeResult> ProbeAsync(
        string path,
        RootPathComparison comparison,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var canonical = new CanonicalLibraryRoot(NormalizeSeparators(fullPath), comparison);
        if (!OperatingSystem.IsWindows() && fullPath.Contains('\\'))
        {
            return ValueTask.FromResult(new LibraryRootProbeResult(LibraryRootProbeStatus.Inaccessible, canonical));
        }

        try
        {
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                return ValueTask.FromResult(
                    new LibraryRootProbeResult(LibraryRootProbeStatus.Missing, canonical));
            }

            if (HasReparseAncestor(fullPath, cancellationToken))
            {
                return ValueTask.FromResult(
                    new LibraryRootProbeResult(LibraryRootProbeStatus.Inaccessible, canonical));
            }

            using var entries = Directory.EnumerateFileSystemEntries(fullPath).GetEnumerator();
            _ = entries.MoveNext();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new LibraryRootProbeResult(LibraryRootProbeStatus.Available, canonical));
        }
        catch (FileNotFoundException)
        {
            return ValueTask.FromResult(
                new LibraryRootProbeResult(LibraryRootProbeStatus.Missing, canonical));
        }
        catch (DirectoryNotFoundException)
        {
            return ValueTask.FromResult(
                new LibraryRootProbeResult(LibraryRootProbeStatus.Missing, canonical));
        }
        catch (DriveNotFoundException)
        {
            return ValueTask.FromResult(
                new LibraryRootProbeResult(LibraryRootProbeStatus.Missing, canonical));
        }
        catch (UnauthorizedAccessException)
        {
            return ValueTask.FromResult(
                new LibraryRootProbeResult(LibraryRootProbeStatus.Inaccessible, canonical));
        }
        catch (IOException)
        {
            return ValueTask.FromResult(
                new LibraryRootProbeResult(LibraryRootProbeStatus.Inaccessible, canonical));
        }
    }

    private static bool HasReparseAncestor(string fullPath, CancellationToken cancellationToken)
    {
        // A lexical alias must not bypass the one-physical-root overlap policy.
        for (var ancestor = new DirectoryInfo(fullPath); ancestor is not null; ancestor = ancestor.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');
}
