using System.Buffers.Binary;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.Modules.PreviewProvider.Infrastructure;

internal static class ImageWorkerTransport
{
    public static async ValueTask<ImageWorkerHeader> ReadHeaderAsync(Stream input, CancellationToken token)
    {
        var bytes = new byte[ImageWorkerProtocol.HeaderBytes];
        await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return ImageWorkerProtocol.ReadHeader(bytes);
    }

    public static void RequireStatus(ImageWorkerHeader frame, ImageWorkerStatus expected)
    {
        if (frame.Status == (int)expected) return;
        throw Rejected((ImageWorkerStatus)frame.Status switch
        {
            ImageWorkerStatus.Invalid => ImagePreviewFailure.Invalid,
            ImageWorkerStatus.Unsupported => ImagePreviewFailure.Unsupported,
            ImageWorkerStatus.Limit => ImagePreviewFailure.LimitExceeded,
            ImageWorkerStatus.SourceChanged => ImagePreviewFailure.SourceChanged,
            _ => ImagePreviewFailure.Unavailable,
        }, "decoder_status");
    }

    public static async Task DrainErrorsAsync(Stream errors, CancellationToken token)
    {
        var buffer = new byte[1024];
        var total = 0;
        int count;
        while ((count = await errors.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > 4096) throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
        }
    }

    public static void ValidatePng(ReadOnlySpan<byte> png, ImageWorkerHeader frame, ImagePreviewVariant variant)
    {
        var edge = ImageWorkerProtocol.MaximumEdge((int)variant);
        if (frame.Profile != (int)variant || frame.Length is < 33 || frame.Length > ImageWorkerProtocol.MaximumOutput((int)variant)
            || frame.Width is < 1 || frame.Width > edge || frame.Height is < 1 || frame.Height > edge
            || png.Length != frame.Length || !png.StartsWith(ImageWorkerProtocol.PngSignature)
            || BinaryPrimitives.ReadInt32BigEndian(png[8..]) != 13 || !png.Slice(12, 4).SequenceEqual("IHDR"u8)
            || BinaryPrimitives.ReadInt32BigEndian(png[16..]) != frame.Width || BinaryPrimitives.ReadInt32BigEndian(png[20..]) != frame.Height
            || png[24] != 8 || png[25] is not (2 or 6))
        {
            throw Rejected(ImagePreviewFailure.Invalid, "png_header");
        }
        if (!PngDerivativeValidator.Valid(png)) throw Rejected(ImagePreviewFailure.Invalid, "png_chunks");
    }

    private static ImagePreviewException Rejected(ImagePreviewFailure failure, string stage)
    {
        var exception = new ImagePreviewException(failure);
        exception.Data["preview_stage"] = stage;
        return exception;
    }
}
