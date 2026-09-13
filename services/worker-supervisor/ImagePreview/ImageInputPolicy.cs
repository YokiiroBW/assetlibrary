using System.Buffers.Binary;
using AssetLibrary.ImagePreview.Protocol;

namespace AssetLibrary.ImagePreview.Worker;

internal static class ImageInputPolicy
{
    private const uint MaximumBufferedPngChunkBytes = 4 * 1024 * 1024;
    public static void Validate(ReadOnlySpan<byte> source)
    {
        if (source.Length > ImageWorkerProtocol.MaximumSourceBytes) throw new ImageDecodeException(ImageWorkerStatus.Limit);
        if (source.StartsWith(ImageWorkerProtocol.PngSignature))
        {
            ValidatePng(source);
        }
        else if (source.Length >= 12 && source[..4].SequenceEqual("RIFF"u8) && source.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            ValidateWebp(source);
        }
        else if (source.Length < 3 || source[0] != 0xff || source[1] != 0xd8 || source[2] != 0xff)
        {
            throw new ImageDecodeException(ImageWorkerStatus.Unsupported);
        }
    }

    private static void ValidatePng(ReadOnlySpan<byte> source)
    {
        if (source.Length < 33 || !source.Slice(12, 4).SequenceEqual("IHDR"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(source[8..]) != 13)
        {
            throw new ImageDecodeException(ImageWorkerStatus.Invalid);
        }

        ValidateDimensions(BinaryPrimitives.ReadUInt32BigEndian(source[16..]), BinaryPrimitives.ReadUInt32BigEndian(source[20..]));
        if (source[24] == 16) throw new ImageDecodeException(ImageWorkerStatus.Unsupported);
        var offset = 8;
        while (offset < source.Length)
        {
            if (source.Length - offset < 12) throw new ImageDecodeException(ImageWorkerStatus.Invalid);
            var length = BinaryPrimitives.ReadUInt32BigEndian(source[offset..]);
            if (length > source.Length - offset - 12) throw new ImageDecodeException(ImageWorkerStatus.Invalid);
            var kind = source.Slice(offset + 4, 4);
            // libpng streams IDAT, but buffers other chunks; cap that copy cost before entering the codec.
            if (length > MaximumBufferedPngChunkBytes && !kind.SequenceEqual("IDAT"u8)) throw new ImageDecodeException(ImageWorkerStatus.Limit);
            if (kind.SequenceEqual("acTL"u8)) throw new ImageDecodeException(ImageWorkerStatus.Unsupported);
            offset += checked((int)length + 12);
        }
    }

    private static void ValidateWebp(ReadOnlySpan<byte> source)
    {
        if ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) + 8 != (ulong)source.Length)
        {
            throw new ImageDecodeException(ImageWorkerStatus.Invalid);
        }

        var offset = 12;
        while (offset < source.Length)
        {
            if (source.Length - offset < 8) throw new ImageDecodeException(ImageWorkerStatus.Invalid);
            var tag = source.Slice(offset, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(source[(offset + 4)..]);
            if (length > source.Length - offset - 8) throw new ImageDecodeException(ImageWorkerStatus.Invalid);
            if (tag.SequenceEqual("ANIM"u8) || tag.SequenceEqual("ANMF"u8)
                || (tag.SequenceEqual("VP8X"u8) && length > 0 && (source[offset + 8] & 2) != 0))
            {
                throw new ImageDecodeException(ImageWorkerStatus.Unsupported);
            }

            offset += checked((int)length + 8 + (int)(length & 1));
        }

        if (offset != source.Length) throw new ImageDecodeException(ImageWorkerStatus.Invalid);
    }

    public static void ValidateDimensions(ulong width, ulong height)
    {
        if (width == 0 || height == 0) throw new ImageDecodeException(ImageWorkerStatus.Invalid);
        if (width > ImageWorkerProtocol.MaximumSourceEdge || height > ImageWorkerProtocol.MaximumSourceEdge
            || width * height > ImageWorkerProtocol.MaximumSourcePixels)
        {
            throw new ImageDecodeException(ImageWorkerStatus.Limit);
        }
    }
}

internal sealed class ImageDecodeException(ImageWorkerStatus status) : Exception("The image cannot be decoded.")
{
    public ImageWorkerStatus Status { get; } = status;
}
