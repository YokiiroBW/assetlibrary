using System.Buffers.Binary;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Modules.PreviewProvider.Infrastructure;

namespace AssetLibrary.ImageSupervisor;

internal static class LocalImageFrames
{
    internal static async Task<ImageWorkerHeader> ReadAsync(Stream stream, CancellationToken token)
    {
        var bytes = new byte[ImageWorkerProtocol.HeaderBytes];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return ImageWorkerProtocol.ReadHeader(bytes);
    }
    internal static bool Empty(ImageWorkerHeader frame, ImageWorkerStatus status) => frame.Status == (int)status
        && frame.Profile == 0 && frame.Length == 0 && frame.Width == 0 && frame.Height == 0;
    internal static void RequireRequest(ImageWorkerHeader frame)
    {
        if (frame.Status != (int)ImageWorkerStatus.Request || frame.Profile is not (0 or 1)
            || frame.Length is < 1 or > ImageWorkerProtocol.MaximumSourceBytes || frame.Width != 0 || frame.Height != 0)
            throw new InvalidDataException("Invalid image request frame.");
    }
    internal static bool IsImageFailure(ImageWorkerHeader frame) => frame.Status is >= 4 and <= 7
        && frame.Profile == 0 && frame.Length == 0 && frame.Width == 0 && frame.Height == 0;
    internal static void RequireOutput(ImageWorkerHeader frame, int profile)
    {
        var edge = ImageWorkerProtocol.MaximumEdge(profile);
        if (frame.Status != (int)ImageWorkerStatus.Success || frame.Profile != profile || frame.Length < 33
            || frame.Length > ImageWorkerProtocol.MaximumOutput(profile) || frame.Width < 1 || frame.Width > edge
            || frame.Height < 1 || frame.Height > edge) throw new InvalidDataException("Invalid image output frame.");
    }
    internal static void RequirePng(byte[] png, ImageWorkerHeader frame)
    {
        if (png.Length != frame.Length || !PngDerivativeValidator.Valid(png)
            || BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) != frame.Width
            || BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) != frame.Height)
            throw new InvalidDataException("Invalid image output PNG.");
    }
    internal static async Task CopyExactlyAsync(Stream from, Stream to, int count, CancellationToken token)
    {
        var buffer = new byte[16384];
        while (count > 0)
        {
            var read = await from.ReadAsync(buffer.AsMemory(0, Math.Min(count, buffer.Length)), token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            await to.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            count -= read;
        }
    }
    internal static Task WriteEmptyAsync(Stream stream, ImageWorkerStatus status, CancellationToken token) =>
        stream.WriteAsync(ImageWorkerProtocol.Header((int)status, 0, 0), token).AsTask();
}
