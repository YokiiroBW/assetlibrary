using System.Buffers;
using System.Security.Cryptography;

using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Infrastructure;

/// <summary>
/// Streams file bytes to a complete SHA-256 digest. It opens sources read-only and share-read,
/// refuses any path that leaves its allowed root or passes through a reparse point, and never
/// returns a digest for a file that changed while it was being read.
/// </summary>
public sealed class SystemDedupContentReader : IDedupContentReader
{
    private const int CopyBufferSize = 128 * 1024;

    /// <summary>Bytes retained from each end for the structural fingerprint.</summary>
    private const int StructureSampleSize = 4096;

    public Task<IReadOnlyList<ContentReadResult>> ReadAsync(
        IReadOnlyList<DedupContentReadRequest> requests,
        int concurrency,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
        return DedupReadWindow.RunAsync<DedupContentReadRequest, ContentReadResult>(
            requests,
            concurrency,
            ReadOneAsync,
            cancellationToken);
    }

    private static async Task<ContentReadResult> ReadOneAsync(
        DedupContentReadRequest request,
        CancellationToken cancellationToken)
    {
        var absolute = DedupPathGuard.Resolve(request.Root, request.RelativePath);
        if (absolute is null)
        {
            return Failure(request, DedupReadFailure.UnsafePath);
        }

        try
        {
            if (!DedupPathGuard.IsPhysicalFile(absolute, out var unusable))
            {
                return Failure(request, unusable);
            }

            var before = new FileInfo(absolute);
            var observedLength = before.Length;
            var observedWriteTime = new DateTimeOffset(before.LastWriteTimeUtc, TimeSpan.Zero);
            if (observedLength > request.MaximumBytes)
            {
                return Failure(request, DedupReadFailure.TooLarge, observedLength, observedWriteTime);
            }

            return await HashAsync(request, absolute, observedLength, observedWriteTime, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(request, DedupReadFailure.PermissionDenied);
        }
        catch (FileNotFoundException)
        {
            return Failure(request, DedupReadFailure.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(request, DedupReadFailure.Missing);
        }
        catch (IOException)
        {
            // A device that went away mid-read is an unreadable source: not a missing file, not a
            // duplicate, and never a reason to forget what a previous scan already observed.
            return Failure(request, DedupReadFailure.Unreadable);
        }
    }

    private static async Task<ContentReadResult> HashAsync(
        DedupContentReadRequest request,
        string absolute,
        long observedLength,
        DateTimeOffset observedWriteTime,
        CancellationToken cancellationToken)
    {
        var sampleSize = (int)Math.Min(StructureSampleSize, observedLength);
        var head = new byte[sampleSize];
        var tail = new byte[sampleSize];
        var pool = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            using var stream = new FileStream(
                absolute,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != observedLength)
            {
                return Failure(request, DedupReadFailure.ChangedDuringRead, observedLength, observedWriteTime);
            }

            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var headWritten = await FillAsync(stream, pool, head, cancellationToken).ConfigureAwait(false);

            // The sampled head is real content and must reach the digest before the tail scan, or a
            // short file would be reported with the digest of an empty stream.
            digest.AppendData(head, 0, headWritten);
            var read = headWritten;
            var tailCarried = 0;
            while (read < observedLength)
            {
                var wanted = (int)Math.Min(pool.Length, observedLength - read);
                var chunk = await stream
                    .ReadAsync(pool.AsMemory(0, wanted), cancellationToken)
                    .ConfigureAwait(false);
                if (chunk <= 0)
                {
                    break;
                }

                digest.AppendData(pool, 0, chunk);
                read += chunk;
                tailCarried = RetainTail(pool, chunk, tail, tailCarried, sampleSize);
            }

            if (observedLength <= headWritten)
            {
                // The whole file was already sampled as its head, so it is also its own tail.
                head.AsSpan(0, headWritten).CopyTo(tail);
                tailCarried = headWritten;
            }

            if (read != observedLength || stream.Position != observedLength)
            {
                return Failure(request, DedupReadFailure.ChangedDuringRead, observedLength, observedWriteTime);
            }

            return new ContentReadResult(
                request.RelativePath,
                observedLength,
                Convert.ToHexStringLower(digest.GetHashAndReset()),
                DedupStructureHash.Compute(
                    observedLength,
                    head.AsSpan(0, headWritten),
                    tail.AsSpan(0, tailCarried)),
                observedWriteTime,
                DedupReadFailure.None);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pool);
        }
    }

    private static async Task<int> FillAsync(
        FileStream stream,
        byte[] pool,
        byte[] destination,
        CancellationToken cancellationToken)
    {
        var written = 0;
        while (written < destination.Length)
        {
            var wanted = Math.Min(pool.Length, destination.Length - written);
            var chunk = await stream
                .ReadAsync(pool.AsMemory(0, wanted), cancellationToken)
                .ConfigureAwait(false);
            if (chunk <= 0)
            {
                break;
            }

            pool.AsSpan(0, chunk).CopyTo(destination.AsSpan(written));
            written += chunk;
        }

        return written;
    }

    /// <summary>
    /// Keeps the most recent bytes seen so far as the tail sample, without buffering the file.
    /// </summary>
    private static int RetainTail(
        byte[] pool,
        int chunk,
        byte[] tail,
        int carried,
        int sampleSize)
    {
        if (sampleSize == 0)
        {
            return 0;
        }

        var source = pool.AsSpan(0, chunk);
        if (source.Length >= sampleSize)
        {
            source[^sampleSize..].CopyTo(tail);
            return sampleSize;
        }

        var retained = Math.Min(carried + source.Length, sampleSize);
        var keepFromCarried = Math.Min(carried, retained - source.Length);
        if (keepFromCarried > 0 && keepFromCarried < carried)
        {
            tail.AsSpan(carried - keepFromCarried, keepFromCarried).CopyTo(tail);
        }

        source.CopyTo(tail.AsSpan(keepFromCarried));
        return retained;
    }

    private static ContentReadResult Failure(
        DedupContentReadRequest request,
        DedupReadFailure failure,
        long length = 0,
        DateTimeOffset lastWriteTimeUtc = default) =>
        new(request.RelativePath, length, null, 0, lastWriteTimeUtc, failure);
}
