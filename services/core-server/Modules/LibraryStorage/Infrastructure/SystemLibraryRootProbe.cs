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
        try
        {
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                return ValueTask.FromResult(
                    new LibraryRootProbeResult(LibraryRootProbeStatus.Missing, canonical));
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

    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');
}
