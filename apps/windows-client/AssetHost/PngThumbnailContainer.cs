using System.Buffers.Binary;

namespace AssetLibrary.Windows.AssetHost;

internal static class PngThumbnailContainer
{
    internal const int MaximumBytes = 2097152;
    internal static (uint Width, uint Height) Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 45 or > MaximumBytes || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        { throw Invalid(); }
        var offset = 8; var chunks = 0; var dataSeen = false; uint width = 0; uint height = 0;
        while (offset < bytes.Length)
        {
            if (++chunks > 4096 || bytes.Length - offset < 12) { throw Invalid(); }
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (length > bytes.Length - offset - 12) { throw Invalid(); }
            var size = (int)length;
            var type = bytes.Slice(offset + 4, 4);
            if (Crc(bytes.Slice(offset + 4, size + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + size + 8)..])) { throw Invalid(); }
            if (chunks == 1)
            {
                if (!type.SequenceEqual("IHDR"u8) || size != 13) { throw Invalid(); }
                (width, height) = Header(bytes.Slice(offset + 8, size));
            }
            else if (type.SequenceEqual("IHDR"u8) || type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
            { throw Invalid(); }
            if (type.SequenceEqual("IDAT"u8)) { dataSeen = true; }
            offset += size + 12;
            if (type.SequenceEqual("IEND"u8))
            {
                if (size != 0 || !dataSeen || offset != bytes.Length) { throw Invalid(); }
                return (width, height);
            }
        }
        throw Invalid();
    }

    private static (uint Width, uint Height) Header(ReadOnlySpan<byte> header)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(header); var height = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        if (width is < 1 or > 512 || height is < 1 or > 512 || (ulong)width * height > 262144
            || header[8] != 8 || header[9] is not (0 or 2 or 3 or 4 or 6) || header[10] != 0 || header[11] != 0 || header[12] > 1)
        { throw Invalid(); }
        return (width, height);
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; ++bit) { crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u); }
        }
        return ~crc;
    }
    private static InvalidDataException Invalid() => new("Invalid bounded PNG thumbnail.");
}
