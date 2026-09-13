using System.Diagnostics;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class PreviewDecoderTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MaximumEdgeAndEncodedSizeDecodeInTheUnchangedColdJobBudget()
    {
        var png = PreviewPngFixture.Create(1600, 12582912);
        Assert.HasCount(12582912, png);
        Assert.ThrowsExactly<InvalidDataException>(() => PngThumbnailContainer.Validate(png));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var watch = Stopwatch.StartNew();
        var pixels = await new ThumbnailDecoder(ThumbnailTestSupport.Executable).DecodePreviewAsync(png, deadline.Token);
        TestContext.WriteLine($"Cold preview helper including parent validation: {watch.ElapsedMilliseconds} ms; PNG {png.Length}; pixels {pixels.Bytes.Length}.");
        Assert.AreEqual(1600u, pixels.Width); Assert.AreEqual(1600u, pixels.Height);
        Assert.HasCount(10240000, pixels.Bytes);
        CollectionAssert.AreEqual(new byte[] { 0, 63, 63, 255 }, pixels.Bytes[^4..]);
    }

    [TestMethod]
    public async Task PreviewAlphaAndProfileIsolationRemainExact()
    {
        var result = await new ThumbnailDecoder(ThumbnailTestSupport.Executable).DecodePreviewAsync(ThumbnailTestSupport.Png, CancellationToken.None);
        CollectionAssert.AreEqual(ThumbnailTestSupport.ExpectedPixels, result.Bytes);
        Assert.AreEqual(2, await ThumbnailDecodeHelper.RunPreviewAsync());
        Assert.ThrowsExactly<InvalidDataException>(() => PngThumbnailContainer.Validate(PreviewPngFixture.Create(1601), DerivedImageProfile.Preview1600));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DerivedImageSpecification.For((DerivedImageProfile)3));
    }

    [TestMethod]
    public void MaximumRawFrameNeverFitsThumbnailAndRejectsNonpremultipliedPixels()
    {
        var request = new ThumbnailRequest(8, Guid.NewGuid(), Guid.NewGuid());
        var response = new ThumbnailResponse(ThumbnailStatus.Ready, request.Epoch, request.Node,
            new ThumbnailPixels(1600, 1600, new byte[10240000]));
        var frame = PreviewProtocol.EncodeResponse(request.RequestId, response);
        Assert.HasCount(10240072, frame);
        Assert.HasCount(10240000, PreviewProtocol.DecodeResponse(frame, request).Image!.Bytes);
        Assert.ThrowsExactly<InvalidDataException>(() => ThumbnailProtocol.EncodeResponse(request.RequestId, response));
        Assert.ThrowsExactly<InvalidDataException>(() => ThumbnailProtocol.DecodeResponse(frame, request));
        frame[^4] = 1;
        Assert.ThrowsExactly<InvalidDataException>(() => PreviewProtocol.DecodeResponse(frame, request));
    }
}
