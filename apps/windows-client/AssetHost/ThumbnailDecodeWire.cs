using System.Buffers.Binary;

namespace AssetLibrary.Windows.AssetHost;

internal static class ThumbnailDecodeWire
{
    internal static async Task WriteInputAsync(Stream pipe, byte[] png, CancellationToken token)
    {
        var length = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)png.Length);
        await pipe.WriteAsync(length, token).ConfigureAwait(false);
        await pipe.WriteAsync(png, token).ConfigureAwait(false);
    }
    internal static async Task<byte[]> ReadInputAsync(Stream pipe, CancellationToken token)
    {
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (size is < 45 or > PngThumbnailContainer.MaximumBytes) { throw new InvalidDataException("Invalid decode input size."); }
        var bytes = new byte[size];
        await pipe.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (await pipe.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0) { throw new InvalidDataException("Unexpected decode input."); }
        return bytes;
    }
    internal static async Task WriteOutputAsync(Stream pipe, ThumbnailPixels pixels, CancellationToken token)
    {
        Validate(pixels);
        var prefix = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, pixels.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(4), pixels.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(8), (uint)pixels.Bytes.Length);
        await pipe.WriteAsync(prefix, token).ConfigureAwait(false);
        await pipe.WriteAsync(pixels.Bytes, token).ConfigureAwait(false);
    }
    internal static async Task<ThumbnailPixels> ReadOutputAsync(Stream pipe, CancellationToken token)
    {
        var prefix = new byte[12]; await pipe.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(prefix); var height = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(4));
        var length = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(8));
        if (length > ThumbnailProtocol.MaximumPixels) { throw new InvalidDataException("Invalid decode output size."); }
        ThumbnailProtocol.ValidateDimensions(width, height, (int)length);
        var bytes = new byte[length]; await pipe.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (await pipe.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0) { throw new InvalidDataException("Unexpected decode output."); }
        var pixels = new ThumbnailPixels(width, height, bytes); Validate(pixels); return pixels;
    }
    private static void Validate(ThumbnailPixels pixels)
    {
        ThumbnailProtocol.ValidateDimensions(pixels.Width, pixels.Height, pixels.Bytes.Length);
        for (var offset = 0; offset != pixels.Bytes.Length; offset += 4)
        {
            var alpha = pixels.Bytes[offset + 3];
            if (pixels.Bytes[offset] > alpha || pixels.Bytes[offset + 1] > alpha || pixels.Bytes[offset + 2] > alpha)
            { throw new InvalidDataException("Decode output is not premultiplied."); }
        }
    }
}
