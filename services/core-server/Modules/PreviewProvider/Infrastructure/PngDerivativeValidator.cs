using System.Buffers.Binary;
using AssetLibrary.ImagePreview.Protocol;

namespace AssetLibrary.Modules.PreviewProvider.Infrastructure;

internal static class PngDerivativeValidator
{
    private static readonly uint[] CrcTable = BuildTable();

    public static bool Valid(ReadOnlySpan<byte> png)
    {
        if (png.Length < 57 || !png.StartsWith(ImageWorkerProtocol.PngSignature)) return false;
        var offset = 8;
        var chunks = 0;
        var header = false;
        var data = false;
        var srgb = false;
        var significantBits = false;
        while (offset < png.Length)
        {
            if (++chunks > 4096 || !ValidChunk(png[offset..], out var count)) return false;
            var kind = png.Slice(offset + 4, 4);
            if (kind.SequenceEqual("IHDR"u8))
            {
                if (header || offset != 8 || !ValidHeader(png.Slice(offset + 8, count))) return false;
                header = true;
            }
            else if (kind.SequenceEqual("sRGB"u8))
            {
                if (!ValidSrgb(png.Slice(offset + 8, count), header, data, srgb)) return false;
                srgb = true;
            }
            else if (kind.SequenceEqual("sBIT"u8))
            {
                if (!ValidSignificantBits(png.Slice(offset + 8, count), header, data, significantBits, png[25])) return false;
                significantBits = true;
            }
            else if (kind.SequenceEqual("IDAT"u8))
            {
                if (!header || count == 0) return false;
                data = true;
            }
            else if (kind.SequenceEqual("IEND"u8))
            {
                return ValidEnd(header, data, count, offset, png.Length);
            }
            else
            {
                // Re-encoded output has no source text/EXIF/ICC, animation, scripting or unknown ancillary data.
                return false;
            }
            offset += count + 12;
        }
        return false;
    }

    private static bool ValidChunk(ReadOnlySpan<byte> chunk, out int count)
    {
        count = 0;
        if (chunk.Length < 12) return false;
        var length = BinaryPrimitives.ReadUInt32BigEndian(chunk);
        if (length > chunk.Length - 12) return false;
        count = checked((int)length);
        return Crc(chunk.Slice(4, count + 4)) == BinaryPrimitives.ReadUInt32BigEndian(chunk[(8 + count)..]);
    }

    private static bool ValidHeader(ReadOnlySpan<byte> header) => header.Length == 13 && header[8] == 8
        && header[9] is 2 or 6 && header[10] == 0 && header[11] == 0 && header[12] == 0;

    private static bool ValidSrgb(ReadOnlySpan<byte> data, bool hasHeader, bool hasPixels, bool hasSrgb) =>
        hasHeader && !hasPixels && !hasSrgb && data.Length == 1 && data[0] <= 3;

    private static bool ValidSignificantBits(ReadOnlySpan<byte> data, bool hasHeader, bool hasPixels, bool alreadyPresent, byte colorType) =>
        hasHeader && !hasPixels && !alreadyPresent && data.Length == (colorType == 6 ? 4 : 3)
        && data.IndexOfAnyExcept((byte)8) < 0;

    private static bool ValidEnd(bool header, bool data, int count, int offset, int length) =>
        header && data && count == 0 && offset + 12 == length;

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint value = 0; value < table.Length; value++)
        {
            var crc = value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
            table[value] = crc;
        }
        return table;
    }
}
