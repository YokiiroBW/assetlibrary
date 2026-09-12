using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ThumbnailDecoderTests
{
    [TestMethod]
    public async Task RealJobRestrictedWicProducesExactPremultipliedPixels()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pixels = await new ThumbnailDecoder(ThumbnailTestSupport.Executable).DecodeAsync(ThumbnailTestSupport.Png, deadline.Token);
        Assert.AreEqual(2u, pixels.Width); Assert.AreEqual(2u, pixels.Height);
        CollectionAssert.AreEqual(ThumbnailTestSupport.ExpectedPixels, pixels.Bytes);
    }

    [TestMethod]
    public async Task DecodeModeFailsClosedWithoutOwnedJob()
    {
        Assert.IsFalse(ThumbnailDecodeJob.CurrentIsBounded());
        Assert.AreEqual(2, await ThumbnailDecodeHelper.RunAsync());
    }

    [TestMethod]
    [DataRow("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAACGFjVEwAAAABAAAAALQt6aAAAAAWSURBVHicY/jPwPAfCBsYgLQDkGAAADchBL1ZTJqmAAAAAElFTkSuQmCC")]
    [DataRow("iVBORw0KGgoAAAANSUhEUgAAAgEAAAACCAYAAADB7Q/mAAAAFklEQVR4nGP4z8DwHwgbGIC0A5BgAAA3IQS9WUyapgAAAABJRU5ErkJggg==")]
    public void AnimatedAndOversizedPngAreRejectedBeforeWic(string encoded)
    { Assert.ThrowsExactly<InvalidDataException>(() => PngThumbnailContainer.Validate(Convert.FromBase64String(encoded))); }

    [TestMethod]
    public void CorruptChunkAndTrailingBytesAreRejected()
    {
        var corrupt = ThumbnailTestSupport.Png; corrupt[50] ^= 1;
        Assert.ThrowsExactly<InvalidDataException>(() => PngThumbnailContainer.Validate(corrupt));
        Assert.ThrowsExactly<InvalidDataException>(() => PngThumbnailContainer.Validate([.. ThumbnailTestSupport.Png, 0]));
    }
}
