using System.Buffers;
using System.Security.Cryptography;
using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Infrastructure;

/// <summary>
/// Streaming SHA-256 over one package file. The buffer is caller-sized and bounded, the byte budget is
/// enforced while reading rather than from a declared length, and the digest is computed over exactly
/// the bytes that were read.
/// </summary>
public sealed class MediaPackageFileHasher : IMediaPackageFileHasher
{
    private readonly int bufferBytes;

    public MediaPackageFileHasher(int bufferBytes)
    {
        if (bufferBytes is < 1 or > MediaPackageInspectionLimits.MaximumStreamBufferBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferBytes));
        }

        this.bufferBytes = bufferBytes;
    }

    public async ValueTask<PayloadFacts> HashAsync(
        string absolutePath,
        long byteLimit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferBytes);
        try
        {
            await using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            // The budget is checked against the real length before any byte is read, so a file that
            // already exceeds it is refused without consuming the remaining allowance.
            if (stream.Length > byteLimit)
            {
                throw new MediaPackageBudgetExceededException();
            }

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            while (true)
            {
                // Each read is limited to what the remaining budget still allows, so the budget bounds the
                // bytes actually pulled from the file rather than being checked after a full buffer was
                // already consumed. A file that grew past the remaining allowance is therefore refused at
                // the exact byte where the allowance ends.
                var allowed = byteLimit - total;
                var window = (int)Math.Min(bufferBytes, allowed);
                if (window <= 0)
                {
                    // The allowance is used up but the stream may still hold data: only a stream that is
                    // really finished may be hashed, otherwise this is a named budget verdict.
                    if (await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
                        .ConfigureAwait(false) != 0)
                    {
                        throw new MediaPackageBudgetExceededException();
                    }

                    break;
                }

                var read = await stream
                    .ReadAsync(buffer.AsMemory(0, window), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                hasher.AppendData(buffer, 0, read);
            }

            var digest = Convert.ToHexStringLower(hasher.GetHashAndReset());
            return new PayloadFacts(total, new Sha256Digest(digest));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>
/// Local volume free-space observation through the platform drive abstraction. It reports only the
/// byte count: no credential, no share path and no media-server state is involved.
/// </summary>
public sealed class MediaPackageVolumeSpaceObserver : IMediaPackageVolumeSpaceObserver
{
    public ValueTask<long?> ObserveAvailableBytesAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root))
            {
                return ValueTask.FromResult<long?>(null);
            }

            var drive = new DriveInfo(root);
            return ValueTask.FromResult<long?>(
                drive.IsReady ? drive.AvailableFreeSpace : null);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            // An unobservable volume is reported as unknown and must be refused by the caller, never
            // guessed as sufficient.
            return ValueTask.FromResult<long?>(null);
        }
    }
}
