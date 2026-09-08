using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Contracts;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.PreviewProvider.Application;

internal sealed class ImagePreviewService(ILibraryScanTargetQuery roots, IImageSourceReader sourceReader, IImageDecoder decoder,
    ILogger<ImagePreviewService> logger)
    : IImagePreviewQuery, IDisposable
{
    private readonly SemaphoreSlim capacity = new(2, 2);
    private readonly ImagePreviewCache cache = new();
    private readonly object lifecycle = new();
    private bool disposed;

    public async ValueTask<IImagePreviewLease> PrepareAsync(ImagePreviewSource source, ImagePreviewVariant variant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Entry.Validate();
        if (source.Entry.Kind != AssetEntryKind.File) throw new ImagePreviewException(ImagePreviewFailure.Unsupported);
        if (source.Entry.ContentLength > ImageWorkerProtocol.MaximumSourceBytes) throw new ImagePreviewException(ImagePreviewFailure.LimitExceeded);
        _ = ImageWorkerProtocol.MaximumEdge((int)variant);
        if (!await capacity.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new ImagePreviewException(ImagePreviewFailure.Busy);
        IImageSourceLease? physical = null;
        try
        {
            var target = await roots.FindAsync(source.LibraryId, cancellationToken).ConfigureAwait(false);
            if (target is null || target.LibraryId != source.LibraryId || target.Availability != StorageAvailability.Online)
            {
                throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
            }
            physical = await sourceReader.OpenAsync(target, source.Entry, cancellationToken).ConfigureAwait(false);
            var key = ImageWorkerProtocol.TransformVersion + ":" + (int)variant + ":" + physical.ContentHash;
            var png = cache.Find(key);
            if (png is null)
            {
                png = await decoder.DecodeAsync(physical, variant, cancellationToken).ConfigureAwait(false);
                await physical.VerifyAsync(cancellationToken).ConfigureAwait(false);
                cache.Store(key, png);
            }
            return new ImagePreviewLease(png, physical, CloseSourceAsync);
        }
        catch (ImageDecoderCleanupPendingException pending)
        {
            // A timed-out native startup cannot be cancelled by abandoning its managed Task.
            // Release the source promptly, but quarantine this admission until startup is really reaped.
            _ = ReleaseAfterCleanupAsync(pending.Completion, physical);
            cancellationToken.ThrowIfCancellationRequested();
            throw new ImagePreviewException(pending.Failure);
        }
        catch
        {
            if (physical is null) Release();
            else await CloseSourceAsync(physical).ConfigureAwait(false);
            throw;
        }
    }

    public void Dispose()
    {
        lock (lifecycle)
        {
            disposed = true;
            capacity.Dispose();
            cache.Clear();
        }
        GC.SuppressFinalize(this);
    }

    private async Task ReleaseAfterCleanupAsync(Task startup, IImageSourceLease? physical)
    {
        try
        {
            var sourceCleanup = physical is null ? Task.CompletedTask : WaitForSourceCleanupAsync(physical);
            await Task.WhenAll(sourceCleanup, startup).ConfigureAwait(false);
            Release();
        }
        catch (Exception failure)
        {
            // Keep the finite slot quarantined if resource cleanup failed; do not admit infinite retries.
            ImagePreviewLog.CleanupFailed(logger, failure.GetType().Name);
        }
    }

    private async ValueTask CloseSourceAsync(IImageSourceLease source)
    {
        try
        {
            await source.DisposeAsync().ConfigureAwait(false);
            Release();
        }
        catch (ImageDecoderCleanupPendingException pending)
        {
            _ = ReleaseAfterCleanupAsync(pending.Completion, null);
        }
        catch (Exception failure)
        {
            ImagePreviewLog.CleanupFailed(logger, failure.GetType().Name);
        }
    }

    private static async Task WaitForSourceCleanupAsync(IImageSourceLease source)
    {
        try { await source.DisposeAsync().ConfigureAwait(false); }
        catch (ImageDecoderCleanupPendingException pending) { await pending.Completion.ConfigureAwait(false); }
    }

    private void Release()
    {
        lock (lifecycle) { if (!disposed) capacity.Release(); }
    }
}

internal sealed class ImagePreviewLease(byte[] png, IImageSourceLease source, Func<IImageSourceLease, ValueTask> cleanup) : IImagePreviewLease
{
    private bool disposed;
    public ReadOnlyMemory<byte> Png => png;
    public ValueTask VerifySourceAsync(CancellationToken cancellationToken) => source.VerifyAsync(cancellationToken);
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await cleanup(source).ConfigureAwait(false);
    }
}

internal static partial class ImagePreviewLog
{
    [LoggerMessage(4601, LogLevel.Error, "A bounded image startup cleanup failed ({FailureType}); its admission remains quarantined.")]
    public static partial void CleanupFailed(ILogger logger, string failureType);
}
