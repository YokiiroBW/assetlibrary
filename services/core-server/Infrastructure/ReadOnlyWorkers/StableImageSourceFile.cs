using System.Security.Cryptography;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

// This synchronous physical boundary runs only inside the killable source worker, never in Host request threads.
internal sealed class StableImageSourceFile : IDisposable
{
    private readonly ImageSourcePath path;
    private readonly List<SafeFileHandle> handles = [];
    private readonly List<ImageSourceStamp> stamps = [];
    private FileStream? stream;
    private byte[]? verifiedHash;

    private StableImageSourceFile(ImageSourcePath path) => this.path = path;

    public static StableImageSourceFile Open(CanonicalLibraryRoot root, RelativeAssetPath relative, CancellationToken token)
    {
        var opened = new StableImageSourceFile(ImageSourcePath.Create(root, relative));
        try
        {
            opened.OpenChain(token);
            return opened;
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    public string CopyVerifiedTo(Stream destination, long expectedLength, DateTimeOffset expectedModified,
        int maximumBytes, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(stream is null, this);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, 32 * 1024 * 1024);
        var initial = stamps[^1];
        if (initial.Length != expectedLength || initial.ModifiedAt != expectedModified)
        {
            throw new ReadOnlyWorkerException("preview_source_changed");
        }

        if (initial.Length > maximumBytes) throw new ReadOnlyWorkerException("preview_source_limit");
        var copiedHash = HashContent(destination, maximumBytes, token);
        VerifyCurrent(token);
        if (!CryptographicOperations.FixedTimeEquals(copiedHash, HashContent(null, maximumBytes, token)))
        {
            throw new ReadOnlyWorkerException("preview_source_changed");
        }

        VerifyCurrent(token);
        verifiedHash = copiedHash;
        return Convert.ToHexStringLower(copiedHash);
    }

    public void VerifyCurrent(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(stream is null, this);
        token.ThrowIfCancellationRequested();
        if (Observe(handles[^1]) != stamps[^1]) throw new ReadOnlyWorkerException("preview_source_changed");
        using var current = new StableImageSourceFile(path);
        current.OpenChain(token);
        for (var index = 0; index < stamps.Count; index++)
        {
            if (!stamps[index].SameIdentity(current.stamps[index]))
            {
                throw new ReadOnlyWorkerException("preview_source_changed");
            }
        }

        if (current.stamps[^1] != stamps[^1]) throw new ReadOnlyWorkerException("preview_source_changed");
        // Existing writable mappings can outlive an ordinary file handle; metadata alone is insufficient.
        if (verifiedHash is not null && !CryptographicOperations.FixedTimeEquals(verifiedHash,
            HashContent(null, 32 * 1024 * 1024, token)))
        {
            throw new ReadOnlyWorkerException("preview_source_changed");
        }

        if (Observe(handles[^1]) != stamps[^1]) throw new ReadOnlyWorkerException("preview_source_changed");
    }

    private byte[] HashContent(Stream? destination, int limit, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(stream is null, this);
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = stream.Read(buffer);
            if (count == 0) break;
            total += count;
            if (total > limit || total > stamps[^1].Length) throw new ReadOnlyWorkerException("preview_source_changed");
            hash.AppendData(buffer, 0, count);
            destination?.Write(buffer, 0, count);
        }

        if (total != stamps[^1].Length) throw new ReadOnlyWorkerException("preview_source_changed");
        return hash.GetHashAndReset();
    }

    private void OpenChain(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var currentPath = path.Anchor;
        AddHandle(OpenNative(null, currentPath, directory: true));
        for (var index = 0; index < path.Components.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            currentPath = Path.Combine(currentPath, path.Components[index]);
            AddHandle(OpenNative(handles[^1], OperatingSystem.IsWindows() ? currentPath : path.Components[index],
                directory: index < path.Components.Count - 1));
        }

        stream = new FileStream(handles[^1], FileAccess.Read, 64 * 1024, isAsync: false);
    }

    private void AddHandle(SafeFileHandle handle)
    {
        handles.Add(handle);
        stamps.Add(Observe(handle));
    }

    private static SafeFileHandle OpenNative(SafeFileHandle? parent, string name, bool directory) =>
        OperatingSystem.IsWindows() ? WindowsImageSourceHandle.Open(name, directory)
        : OperatingSystem.IsLinux() ? LinuxImageSourceHandle.Open(parent, name, directory)
        : throw new ReadOnlyWorkerException("preview_source_unavailable");

    private static ImageSourceStamp Observe(SafeFileHandle handle) =>
        OperatingSystem.IsWindows() ? WindowsImageSourceHandle.Observe(handle)
        : OperatingSystem.IsLinux() ? LinuxImageSourceHandle.Observe(handle)
        : throw new ReadOnlyWorkerException("preview_source_unavailable");

    public void Dispose()
    {
        stream?.Dispose();
        stream = null;
        for (var index = handles.Count - 1; index >= 0; index--) handles[index].Dispose();
        handles.Clear();
        GC.SuppressFinalize(this);
    }
}
