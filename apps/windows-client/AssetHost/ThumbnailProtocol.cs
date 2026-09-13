using AssetLibrary.Windows.Client;

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
    public static byte[] EncodeRequest(ThumbnailRequest request) => ImageProjectionProtocol.EncodeRequest(DerivedImageProfile.Thumbnail512, request);
    public static ThumbnailRequest DecodeRequest(ReadOnlySpan<byte> frame) => ImageProjectionProtocol.DecodeRequest(DerivedImageProfile.Thumbnail512, frame);
    public static byte[] EncodeResponse(uint id, ThumbnailResponse response) => ImageProjectionProtocol.EncodeResponse(DerivedImageProfile.Thumbnail512, id, response);
    public static ThumbnailResponse DecodeResponse(ReadOnlySpan<byte> frame, ThumbnailRequest request) => ImageProjectionProtocol.DecodeResponse(DerivedImageProfile.Thumbnail512, frame, request);
    internal static void ValidateDimensions(uint width, uint height, int length) => ImageProjectionProtocol.ValidateDimensions(width, height, length, DerivedImageProfile.Thumbnail512);
}
