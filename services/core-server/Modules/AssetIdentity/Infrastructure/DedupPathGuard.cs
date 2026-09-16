using System.Buffers.Binary;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Infrastructure;

/// <summary>
/// Resolves an allowed root plus a relative asset path into one physical file, or refuses. Every
/// refusal is a named failure so a preview can report "not read" instead of guessing, and the
/// guards are public so the read boundary can be asserted directly rather than only end to end.
/// </summary>
public static class DedupPathGuard
{
    public static string? Resolve(CanonicalLibraryRoot root, RelativeAssetPath relativePath)
    {
        var relative = relativePath.Value;
        if (relative.Length == 0
            || relative.StartsWith('/')
            || relative.Contains('\\')
            || relative.Contains(':')
            || relative.Contains('\0'))
        {
            return null;
        }

        var segments = relative.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            return null;
        }

        var resolvedRoot = Path.GetFullPath(root.Value);
        var combined = Path.GetFullPath(Path.Combine([resolvedRoot, .. segments]));
        var prefix = resolvedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? resolvedRoot
            : $"{resolvedRoot}{Path.DirectorySeparatorChar}";
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return combined.StartsWith(prefix, comparison) ? combined : null;
    }

    /// <summary>
    /// True only when the file and every ancestor directory are real, non-reparse entries. A
    /// junction or symlink anywhere in the path would let a relative path inside the allowed root
    /// resolve to content outside it, so the read is refused instead of followed.
    /// </summary>
    public static bool IsPhysicalFile(string absolute, out DedupReadFailure failure)
    {
        var directory = new FileInfo(absolute).Directory;
        while (directory is not null)
        {
            if (!TryReadAttributes(directory.FullName, out var attributes, out failure))
            {
                return false;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                failure = DedupReadFailure.SourceHasNoReparsePointFreeParents;
                return false;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                failure = DedupReadFailure.UnsafePath;
                return false;
            }

            directory = directory.Parent;
        }

        if (!TryReadAttributes(absolute, out var fileAttributes, out failure))
        {
            return false;
        }

        if ((fileAttributes & FileAttributes.ReparsePoint) != 0)
        {
            // A reparse point is its own entity here: following it could read content owned by
            // another directory, another library or another user.
            failure = DedupReadFailure.UnsafePath;
            return false;
        }

        if ((fileAttributes & FileAttributes.Directory) != 0)
        {
            failure = DedupReadFailure.Unreadable;
            return false;
        }

        failure = DedupReadFailure.None;
        return true;
    }

    private static bool TryReadAttributes(
        string path,
        out FileAttributes attributes,
        out DedupReadFailure failure)
    {
        try
        {
            attributes = File.GetAttributes(path);
            failure = DedupReadFailure.None;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            attributes = default;
            failure = DedupReadFailure.PermissionDenied;
            return false;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            failure = DedupReadFailure.Missing;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            failure = DedupReadFailure.Missing;
            return false;
        }
        catch (IOException)
        {
            attributes = default;
            failure = DedupReadFailure.Unreadable;
            return false;
        }
    }
}

/// <summary>
/// Length plus a sampled edge fingerprint. It is deliberately not a content identity: it only feeds
/// the "did this file change at all" comparison next to the complete strong hash.
/// </summary>
internal static class DedupStructureHash
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;

    private const ulong FnvPrime = 1099511628211UL;

    public static long Compute(long length, ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail)
    {
        var hash = FnvOffsetBasis;
        Fold(ref hash, length);
        Fold(ref hash, head.Length);
        Fold(ref hash, tail.Length);
        foreach (var value in head)
        {
            hash = (hash ^ value) * FnvPrime;
        }

        foreach (var value in tail)
        {
            hash = (hash ^ value) * FnvPrime;
        }

        return unchecked((long)hash);
    }

    private static void Fold(ref ulong hash, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        foreach (var item in buffer)
        {
            hash = (hash ^ item) * FnvPrime;
        }
    }
}
