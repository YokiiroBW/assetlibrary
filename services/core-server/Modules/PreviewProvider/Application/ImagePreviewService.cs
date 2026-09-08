using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.Modules.PreviewProvider.Application;

internal sealed class ImagePreviewService(ILibraryScanTargetQuery roots, IImageSourceReader sourceReader, IImageDecoder decoder)
    : IImagePreviewQuery, IDisposable
{
    private readonly SemaphoreSlim capacity = new(2, 2);
    private readonly ImagePreviewCache cache = new();

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
            return new ImagePreviewLease(png, physical, capacity);
        }
        catch
        {
            try { if (physical is not null) await physical.DisposeAsync().ConfigureAwait(false); }
            finally { capacity.Release(); }
            throw;
        }
    }

    public void Dispose()
    {
        capacity.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class ImagePreviewLease(byte[] png, IImageSourceLease source, SemaphoreSlim capacity) : IImagePreviewLease
{
    private bool disposed;
    public ReadOnlyMemory<byte> Png => png;
    public ValueTask VerifySourceAsync(CancellationToken cancellationToken) => source.VerifyAsync(cancellationToken);
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { await source.DisposeAsync().ConfigureAwait(false); }
        finally { capacity.Release(); }
    }
}
