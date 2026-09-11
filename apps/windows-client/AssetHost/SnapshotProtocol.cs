using System.Buffers.Binary;
using System.Text;

namespace AssetLibrary.Windows.AssetHost;

public enum SnapshotStatus : uint { Ready, Loading, Unavailable, AccessDenied, Expired, InvalidResponse, Busy }
public enum SnapshotKind : ushort { Library = 1, Directory, File, Reparse, NextPage }
public sealed record SnapshotItem(Guid Node, SnapshotKind Kind, string Name);
public sealed record SnapshotRequest(uint RequestId, Guid Epoch, Guid Node);
public sealed record SnapshotResponse(SnapshotStatus Status, Guid Epoch, IReadOnlyList<SnapshotItem> Items);

public static class SnapshotProtocol
{
    public const int HeaderSize = 16;
    public const int MaximumPayload = 65536;
    public const int MaximumItems = 101;
    private const uint Magic = 0x31534C41;
    private static readonly UnicodeEncoding StrictUnicode = new(false, false, true);

    public static byte[] EncodeRequest(SnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var bytes = Header(1, 32, request.RequestId);
        request.Epoch.TryWriteBytes(bytes.AsSpan(16));
        request.Node.TryWriteBytes(bytes.AsSpan(32));
        return bytes;
    }

    public static SnapshotRequest DecodeRequest(ReadOnlySpan<byte> bytes)
    {
        var requestId = ValidateHeader(bytes, 1, 32);
        var request = new SnapshotRequest(requestId, new Guid(bytes.Slice(16, 16)), new Guid(bytes.Slice(32, 16)));
        ValidateRequest(request);
        return request;
    }

    public static byte[] EncodeResponse(uint requestId, SnapshotResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ValidateResponse(response);
        var length = 24 + response.Items.Sum(item => 20 + item.Name.Length * 2);
        if (length > MaximumPayload) { throw InvalidPacket(); }
        var bytes = Header(2, length, requestId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)response.Status);
        response.Epoch.TryWriteBytes(bytes.AsSpan(20));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), (uint)response.Items.Count);
        var offset = 40;
        foreach (var item in response.Items)
        {
            item.Node.TryWriteBytes(bytes.AsSpan(offset));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 16), (ushort)item.Kind);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 18), (ushort)item.Name.Length);
            StrictUnicode.GetBytes(item.Name, bytes.AsSpan(offset + 20));
            offset += 20 + item.Name.Length * 2;
        }
        return bytes;
    }

    public static SnapshotResponse DecodeResponse(ReadOnlySpan<byte> bytes, uint requestId)
    {
        if (bytes.Length < 40 || ValidateHeader(bytes, 2, bytes.Length - HeaderSize) != requestId) { throw InvalidPacket(); }
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[36..]);
        if (count > MaximumItems) { throw InvalidPacket(); }
        var items = new List<SnapshotItem>((int)count);
        var offset = 40;
        for (var index = 0; index < count; ++index)
        {
            if (bytes.Length - offset < 20) { throw InvalidPacket(); }
            var units = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 18)..]);
            if (units is < 1 or > 255 || bytes.Length - offset - 20 < units * 2) { throw InvalidPacket(); }
            var name = StrictUnicode.GetString(bytes.Slice(offset + 20, units * 2));
            items.Add(new SnapshotItem(new Guid(bytes.Slice(offset, 16)),
                (SnapshotKind)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 16)..]), name));
            offset += 20 + units * 2;
        }
        if (offset != bytes.Length) { throw InvalidPacket(); }
        var response = new SnapshotResponse((SnapshotStatus)BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]),
            new Guid(bytes.Slice(20, 16)), items.AsReadOnly());
        ValidateResponse(response);
        return response;
    }

    public static void ValidateName(string name)
    {
        if (name.Length is < 1 or > 255 || name.Any(char.IsControl)) { throw InvalidPacket(); }
        _ = StrictUnicode.GetByteCount(name);
    }

    internal static void ValidateRequestHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic
            || BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != 1
            || BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != 32
            || BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) == 0) { throw InvalidPacket(); }
    }

    private static uint ValidateHeader(ReadOnlySpan<byte> bytes, ushort type, int payload)
    {
        if (bytes.Length < HeaderSize || payload > MaximumPayload || payload < 0 || bytes.Length != HeaderSize + payload
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != type
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != payload
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) == 0) { throw InvalidPacket(); }
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
    }

    private static byte[] Header(ushort type, int payload, uint requestId)
    {
        if (requestId == 0) { throw InvalidPacket(); }
        var bytes = new byte[HeaderSize + payload];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)payload);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), requestId);
        return bytes;
    }

    private static void ValidateRequest(SnapshotRequest request)
    {
        if (request.RequestId == 0 || (request.Epoch == Guid.Empty) != (request.Node == Guid.Empty)) { throw InvalidPacket(); }
    }

    private static void ValidateResponse(SnapshotResponse response)
    {
        if (!Enum.IsDefined(response.Status) || response.Epoch == Guid.Empty || response.Items.Count > MaximumItems
            || (response.Status != SnapshotStatus.Ready && response.Items.Count != 0)
            || response.Items.Count(item => item.Kind == SnapshotKind.NextPage) > 1
            || response.Items.Count(item => item.Kind != SnapshotKind.NextPage) > 100) { throw InvalidPacket(); }
        var tokens = new HashSet<Guid>();
        foreach (var item in response.Items)
        {
            if (item.Node == Guid.Empty || !tokens.Add(item.Node) || !Enum.IsDefined(item.Kind)) { throw InvalidPacket(); }
            ValidateName(item.Name);
        }
    }

    private static InvalidDataException InvalidPacket() => new("Invalid snapshot packet.");
}
