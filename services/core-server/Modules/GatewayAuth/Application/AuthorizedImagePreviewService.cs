using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public sealed class AuthorizedImagePreviewService(ReadOnlyBrowseService reads, IImagePreviewQuery previews)
{
    public async ValueTask<IImagePreviewLease> GetAsync(GetEntryQuery request, ImagePreviewVariant variant, CancellationToken cancellationToken)
    {
        var detail = await ReadAsync(request, cancellationToken).ConfigureAwait(false);
        var entry = detail.Entry;
        var source = new ImagePreviewSource(detail.Library.LibraryId,
            new AssetObservation(entry.EntryId, entry.RelativePath, entry.Kind, entry.ContentLength, entry.LastWriteTimeUtc));
        var lease = await previews.PrepareAsync(source, variant, cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadAsync(request, cancellationToken).ConfigureAwait(false);
            if (current.Entry != entry) throw new ImagePreviewException(ImagePreviewFailure.SourceChanged);
            await lease.VerifySourceAsync(cancellationToken).ConfigureAwait(false);
            return lease;
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<AuthorizedEntryDetail> ReadAsync(GetEntryQuery request, CancellationToken cancellationToken)
    {
        var entry = await reads.GetEntryAsync(request, cancellationToken).ConfigureAwait(false)
            ?? throw new ImagePreviewException(ImagePreviewFailure.NotFound);
        if (entry.Library.Availability != StorageAvailability.Online)
        {
            throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
        }

        return entry;
    }
}
