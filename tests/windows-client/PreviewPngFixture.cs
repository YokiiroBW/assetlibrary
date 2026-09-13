using System.Buffers.Binary;
using System.IO.Compression;

namespace AssetLibrary.Windows.Tests;

// Independent synthetic RGBA corpus: filter 0, uncompressed zlib, deterministic pixels, no source asset.
internal static class PreviewPngFixture
{
    internal static byte[] Create(int edge, int? exactLength = null)
    {
        var scanlines = new byte[(edge * 4 + 1) * edge];
        for (var y = 0; y < edge; ++y)
        {
            for (var x = 0; x < edge; ++x)
            {
                var offset = y * (edge * 4 + 1) + 1 + x * 4;
                scanlines[offset] = (byte)(x % 256); scanlines[offset + 1] = (byte)(y % 256);
                scanlines[offset + 2] = 0; scanlines[offset + 3] = 255;
            }
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true)) { zlib.Write(scanlines); }
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, edge); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), edge);
        header[8] = 8; header[9] = 6; Chunk(png, "IHDR"u8, header);
        Chunk(png, "IDAT"u8, compressed.ToArray());
        if (exactLength is { } length) { Chunk(png, "alPX"u8, new byte[checked(length - (int)png.Length - 24)]); }
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }
    private static void Chunk(Stream output, ReadOnlySpan<byte> kind, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number); output.Write(kind); output.Write(data);
        uint remainder = 0xffffffff;
        foreach (var octet in kind.ToArray().Concat(data))
        {
            remainder ^= octet;
            for (var shift = 8; shift > 0; --shift) { remainder = (remainder & 1) == 0 ? remainder >> 1 : (remainder >> 1) ^ 3988292384; }
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, remainder ^ 0xffffffff); output.Write(number);
    }
}
