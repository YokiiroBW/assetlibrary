using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.Modules.PreviewProvider.Application;

internal interface IImageSourceReader
{
    ValueTask<IImageSourceLease> OpenAsync(LibraryScanTarget target, AssetObservation entry, CancellationToken cancellationToken);
}

internal interface IImageSourceLease : IAsyncDisposable
{
    long Length { get; }
    string ContentHash { get; }
    ValueTask CopyToAsync(Stream output, CancellationToken cancellationToken);
    ValueTask VerifyAsync(CancellationToken cancellationToken);
}

internal interface IImageDecoder
{
    ValueTask<byte[]> DecodeAsync(IImageSourceLease source, ImagePreviewVariant variant, CancellationToken cancellationToken);
}
