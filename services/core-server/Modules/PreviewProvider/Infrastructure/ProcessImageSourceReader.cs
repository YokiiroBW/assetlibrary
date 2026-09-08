using System.Security.Cryptography;
using System.Text.Json;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Application;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.Modules.PreviewProvider.Infrastructure;

internal sealed class ProcessImageSourceReader(ReadOnlyWorkerProcessOptions options) : IImageSourceReader
{
    public async ValueTask<IImageSourceLease> OpenAsync(LibraryScanTarget target, AssetObservation entry, CancellationToken cancellationToken)
    {
        var process = ImageChildProcess.Start(ReadOnlyWorkerProcess.CreateStartInfo(options, "preview-source"), cancellationToken);
        var diagnostics = ImageWorkerTransport.DrainErrorsAsync(process.Error, cancellationToken);
        try
        {
            var request = new ImageSourceRequest(target.Root.Value, target.Root.Comparison, entry.RelativePath.Value,
                entry.ContentLength!.Value, entry.LastWriteTimeUtc);
            var payload = JsonSerializer.SerializeToUtf8Bytes(request, ImageSourceJsonContext.Default.ImageSourceRequest);
            if (payload.Length > ReadOnlyWorkerProtocol.FrameLimit) throw new ImagePreviewException(ImagePreviewFailure.LimitExceeded);
            await process.Input.WriteAsync(ImageWorkerProtocol.Header((int)ImageWorkerStatus.Request, 0, payload.Length), cancellationToken).ConfigureAwait(false);
            await process.Input.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await process.Input.FlushAsync(cancellationToken).ConfigureAwait(false);
            var ready = await ImageWorkerTransport.ReadHeaderAsync(process.Output, cancellationToken).ConfigureAwait(false);
            ImageWorkerTransport.RequireStatus(ready, ImageWorkerStatus.Ready);
            if (ready.Length < 0 || ready.Length > ImageWorkerProtocol.MaximumSourceBytes || ready.Width != 32 || ready.Height != 0)
            {
                throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
            }
            var digest = new byte[32];
            await process.Output.ReadExactlyAsync(digest, cancellationToken).ConfigureAwait(false);
            return new ProcessImageSourceLease(process, ready.Length, digest, diagnostics);
        }
        catch
        {
            await process.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

internal sealed class ProcessImageSourceLease(ImageChildProcess process, long length, byte[] digest, Task diagnostics) : IImageSourceLease
{
    private bool disposed;
    public long Length => length;
    public string ContentHash => Convert.ToHexStringLower(digest);

    public async ValueTask CopyToAsync(Stream output, CancellationToken cancellationToken)
    {
        await CommandAsync(1, cancellationToken).ConfigureAwait(false);
        var remaining = length;
        var buffer = new byte[64 * 1024];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (remaining > 0)
        {
            var count = (int)Math.Min(remaining, buffer.Length);
            await process.Output.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            remaining -= count;
        }
        if (!CryptographicOperations.FixedTimeEquals(digest, hash.GetHashAndReset())) throw new ImagePreviewException(ImagePreviewFailure.SourceChanged);
        await AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask VerifyAsync(CancellationToken cancellationToken)
    {
        await CommandAsync(2, cancellationToken).ConfigureAwait(false);
        await AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask CommandAsync(byte command, CancellationToken cancellationToken)
    {
        if (diagnostics.IsFaulted) await diagnostics.ConfigureAwait(false);
        await process.Input.WriteAsync(new[] { command }, cancellationToken).ConfigureAwait(false);
        await process.Input.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AcknowledgeAsync(CancellationToken cancellationToken)
    {
        var response = await ImageWorkerTransport.ReadHeaderAsync(process.Output, cancellationToken).ConfigureAwait(false);
        ImageWorkerTransport.RequireStatus(response, ImageWorkerStatus.Success);
        if (response.Length != 0 || response.Width != 0 || response.Height != 0) throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
        if (diagnostics.IsFaulted) await diagnostics.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await process.DisposeAsync().ConfigureAwait(false);
        try { await diagnostics.ConfigureAwait(false); }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Closing the owned pipe is expected during cancellation; malformed diagnostics are not swallowed.
        }
    }
}
