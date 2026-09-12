using System.Buffers.Binary;

namespace AssetLibrary.Windows.AssetHost;

public enum ThumbnailStatus : uint { Ready, Loading, Unavailable, AccessDenied, Expired, InvalidResponse, Busy, Unsupported }
public sealed record ThumbnailRequest(uint RequestId, Guid Epoch, Guid Node);
public sealed record ThumbnailPixels(uint Width, uint Height, byte[] Bytes);
public sealed record ThumbnailResponse(ThumbnailStatus Status, Guid Epoch, Guid Node, ThumbnailPixels? Image = null)
{
    internal CancellationToken Validity { get; init; }
}

public static class ThumbnailProtocol
{
    public const int HeaderSize = 16;
    public const int PrefixSize = 56;
    public const int MaximumPixels = 1048576;
    public const int MaximumPayload = PrefixSize + MaximumPixels;
    private const uint Magic = 0x31474C41;

    public static byte[] EncodeRequest(ThumbnailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Epoch == Guid.Empty || request.Node == Guid.Empty) { throw Invalid(); }
        var frame = Header(1, 32, request.RequestId);
        request.Epoch.TryWriteBytes(frame.AsSpan(16)); request.Node.TryWriteBytes(frame.AsSpan(32));
        return frame;
    }

    public static ThumbnailRequest DecodeRequest(ReadOnlySpan<byte> frame)
    {
        var id = CheckHeader(frame, 1, 32);
        var request = new ThumbnailRequest(id, new Guid(frame.Slice(16, 16)), new Guid(frame.Slice(32, 16)));
        if (request.Epoch == Guid.Empty || request.Node == Guid.Empty) { throw Invalid(); }
        return request;
    }

    public static byte[] EncodeResponse(uint requestId, ThumbnailResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Validate(response);
        var bytes = Header(2, PrefixSize + (response.Image?.Bytes.Length ?? 0), requestId);
        var body = bytes.AsSpan(HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)response.Status);
        response.Epoch.TryWriteBytes(body[4..]); response.Node.TryWriteBytes(body[20..]);
        if (response.Image is { } pixels)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(body[36..], pixels.Width);
            BinaryPrimitives.WriteUInt32LittleEndian(body[40..], pixels.Height);
            BinaryPrimitives.WriteUInt32LittleEndian(body[44..], pixels.Width * 4);
            BinaryPrimitives.WriteUInt32LittleEndian(body[48..], 1);
            BinaryPrimitives.WriteUInt32LittleEndian(body[52..], (uint)pixels.Bytes.Length);
            pixels.Bytes.CopyTo(body[PrefixSize..]);
        }
        return bytes;
    }

    public static ThumbnailResponse DecodeResponse(ReadOnlySpan<byte> frame, ThumbnailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (frame.Length < HeaderSize + PrefixSize || CheckHeader(frame, 2, frame.Length - HeaderSize) != request.RequestId) { throw Invalid(); }
        var body = frame[HeaderSize..];
        var status = (ThumbnailStatus)BinaryPrimitives.ReadUInt32LittleEndian(body);
        var epoch = new Guid(body.Slice(4, 16)); var node = new Guid(body.Slice(20, 16));
        var width = BinaryPrimitives.ReadUInt32LittleEndian(body[36..]); var height = BinaryPrimitives.ReadUInt32LittleEndian(body[40..]);
        var stride = BinaryPrimitives.ReadUInt32LittleEndian(body[44..]); var format = BinaryPrimitives.ReadUInt32LittleEndian(body[48..]);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(body[52..]);
        if (node != request.Node || body.Length != PrefixSize + (long)length) { throw Invalid(); }
        ThumbnailPixels? pixels = null;
        if (status == ThumbnailStatus.Ready)
        {
            if (epoch != request.Epoch || format != 1 || stride != (ulong)width * 4 || length != (ulong)stride * height) { throw Invalid(); }
            ValidateDimensions(width, height, (int)length);
            pixels = new ThumbnailPixels(width, height, body[PrefixSize..].ToArray());
        }
        else if (width != 0 || height != 0 || stride != 0 || format != 0 || length != 0) { throw Invalid(); }
        var response = new ThumbnailResponse(status, epoch, node, pixels);
        Validate(response);
        return response;
    }

    internal static void ValidateDimensions(uint width, uint height, int length)
    {
        if (width is < 1 or > 512 || height is < 1 or > 512 || (ulong)width * height * 4 != (ulong)length || length > MaximumPixels)
        { throw Invalid(); }
    }

    private static void Validate(ThumbnailResponse response)
    {
        if (!Enum.IsDefined(response.Status) || response.Epoch == Guid.Empty || response.Node == Guid.Empty
            || (response.Status == ThumbnailStatus.Ready) != (response.Image is not null)) { throw Invalid(); }
        if (response.Image is { } image) { ValidateDimensions(image.Width, image.Height, image.Bytes.Length); }
    }
    private static byte[] Header(ushort type, int length, uint id)
    {
        if (id == 0 || length > MaximumPayload) { throw Invalid(); }
        var bytes = new byte[HeaderSize + length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        return bytes;
    }
    private static uint CheckHeader(ReadOnlySpan<byte> bytes, ushort type, int length)
    {
        if (bytes.Length != HeaderSize + length || length > MaximumPayload || bytes.Length < HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != type || BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != length
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) == 0) { throw Invalid(); }
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
    }
    private static InvalidDataException Invalid() => new("Invalid thumbnail frame.");
}
