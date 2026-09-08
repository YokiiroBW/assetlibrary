using System.Buffers.Binary;

namespace AssetLibrary.ImagePreview.Protocol;

// This BCL-only source is shared by compilation; the Core never references the Skia decoder assembly.
internal static class ImageWorkerProtocol
{
    public const uint Magic = 0x31495041;
    public const int HeaderBytes = 24;
    public const int MaximumSourceBytes = 32 * 1024 * 1024;
    public const int MaximumSourcePixels = 40_000_000;
    public const int MaximumSourceEdge = 16_384;
    public const int ThumbnailEdge = 512;
    public const int PreviewEdge = 1600;
    public const int ThumbnailBytes = 2 * 1024 * 1024;
    public const int PreviewBytes = 12 * 1024 * 1024;
    public const string TransformVersion = "skia-4.151.2-png8-srgb-orient-fit-v1";
    public static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static int MaximumEdge(int profile) => profile switch
    {
        0 => ThumbnailEdge,
        1 => PreviewEdge,
        _ => throw new InvalidDataException("Invalid image profile."),
    };

    public static int MaximumOutput(int profile) => profile == 0 ? ThumbnailBytes : profile == 1
        ? PreviewBytes : throw new InvalidDataException("Invalid image profile.");

    public static byte[] Header(int status, int profile, int length, int width = 0, int height = 0)
    {
        var bytes = new byte[HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), status);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), profile);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    public static ImageWorkerHeader ReadHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
        {
            throw new InvalidDataException("Invalid image frame.");
        }

        return new ImageWorkerHeader(BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]));
    }
}

internal readonly record struct ImageWorkerHeader(int Status, int Profile, int Length, int Width, int Height);

internal enum ImageWorkerStatus
{
    Ready = 1,
    Request = 2,
    Success = 3,
    Invalid = 4,
    Unsupported = 5,
    Limit = 6,
    Unavailable = 7,
}
