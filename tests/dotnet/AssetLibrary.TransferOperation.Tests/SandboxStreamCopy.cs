using System.Security.Cryptography;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed record SandboxCopyResult(
    PayloadFacts Facts,
    int Chunks,
    int MaximumChunk);

internal sealed class SandboxStreamCopy(int bufferSize = 64 * 1024)
{
    private readonly byte[] buffer = new byte[ValidateBufferSize(bufferSize)];

    public Action<int>? ChunkObserver { get; set; }

    public async ValueTask<SandboxCopyResult> CopyAsync(
        string source,
        string stage,
        CancellationToken cancellationToken)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            stage,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        long length = 0;
        var chunks = 0;
        var maximum = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            digest.AppendData(buffer, 0, read);
            length += read;
            chunks++;
            maximum = Math.Max(maximum, read);
            ChunkObserver?.Invoke(chunks);
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
        var facts = new PayloadFacts(
            length,
            new Sha256Digest(Convert.ToHexStringLower(digest.GetHashAndReset())));
        return new(facts, chunks, maximum);
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
