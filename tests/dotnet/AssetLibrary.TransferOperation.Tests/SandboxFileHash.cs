using System.Security.Cryptography;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class SandboxFileHash(int bufferSize = 64 * 1024)
{
    private readonly byte[] buffer = new byte[ValidateBufferSize(bufferSize)];

    public int ChunksRead { get; private set; }

    public int MaximumObservedChunk { get; private set; }

    public async ValueTask<PayloadFacts> ComputeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ChunksRead = 0;
        MaximumObservedChunk = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            digest.AppendData(buffer, 0, read);
            length += read;
            ChunksRead++;
            MaximumObservedChunk = Math.Max(MaximumObservedChunk, read);
        }

        return new(
            length,
            new Sha256Digest(Convert.ToHexStringLower(digest.GetHashAndReset())));
    }

    private static int ValidateBufferSize(int value)
    {
        if (value is < 4096 or > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value;
    }
}
