using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

// The DTOs describe the same opaque identity/PBGRA shape; this entry point fixes ALP1 and preview1600 limits.
public static class PreviewProtocol
{
    public const int HeaderSize = 16;
    public const int PrefixSize = 56;
    public const int MaximumPixels = 10240000;
    public const int MaximumPayload = PrefixSize + MaximumPixels;
    public static byte[] EncodeRequest(ThumbnailRequest request) => ImageProjectionProtocol.EncodeRequest(DerivedImageProfile.Preview1600, request);
    public static ThumbnailRequest DecodeRequest(ReadOnlySpan<byte> frame) => ImageProjectionProtocol.DecodeRequest(DerivedImageProfile.Preview1600, frame);
    public static byte[] EncodeResponse(uint id, ThumbnailResponse response) => ImageProjectionProtocol.EncodeResponse(DerivedImageProfile.Preview1600, id, response);
    public static ThumbnailResponse DecodeResponse(ReadOnlySpan<byte> frame, ThumbnailRequest request) => ImageProjectionProtocol.DecodeResponse(DerivedImageProfile.Preview1600, frame, request);
}
