using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.PreviewProvider.Contracts;

public enum ImagePreviewVariant
{
    Thumbnail = 0,
    Preview = 1,
}

// Physical facts, not an authorization credential. Network adapters must use GatewayAuth's authorized use case.
public sealed record ImagePreviewSource(LibraryId LibraryId, AssetObservation Entry);

public interface IImagePreviewQuery
{
    ValueTask<IImagePreviewLease> PrepareAsync(ImagePreviewSource source, ImagePreviewVariant variant, CancellationToken cancellationToken);
}

public interface IImagePreviewLease : IAsyncDisposable
{
    ReadOnlyMemory<byte> Png { get; }
    ValueTask VerifySourceAsync(CancellationToken cancellationToken);
}

public enum ImagePreviewFailure
{
    NotFound,
    SourceChanged,
    Unsupported,
    Invalid,
    LimitExceeded,
    Busy,
    Unavailable,
    Timeout,
}

public sealed class ImagePreviewException(ImagePreviewFailure failure) : InvalidOperationException("The derived image request failed.")
{
    public ImagePreviewFailure Failure { get; } = failure;
}
