using System.Buffers.Binary;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ThumbnailLiveTests
{
    [TestMethod]
    [TestCategory("NativeLive")]
    public async Task RealCoreDerivedPngTravelsThroughOwnedWicJobAndTestPipe()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var fixture = await ThumbnailLiveFixture.OpenAsync(deadline.Token);
        foreach (var name in new[] { "landscape.png", "transparent.png" })
        {
            var item = fixture.Page.Items.Single(item => item.Name == name);
            var response = await ThumbnailLiveSupport.ReadPipeAsync(fixture.Endpoint, new ThumbnailRequest(31, fixture.Page.Epoch, item.Node), deadline.Token);
            Assert.AreEqual(ThumbnailStatus.Ready, response.Status);
            Assert.IsNotNull(response.Image);
            Assert.IsLessThanOrEqualTo(512u, response.Image.Width);
            Assert.IsLessThanOrEqualTo(512u, response.Image.Height);
            Assert.AreEqual((long)response.Image.Width * response.Image.Height * 4, response.Image.Bytes.LongLength);
        }
    }
}
