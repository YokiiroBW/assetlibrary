using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ThumbnailLiveTests
{
    [TestMethod]
    [TestCategory("NativeLive")]
    [DoNotParallelize]
    public async Task RealCoreDerivedPngTravelsThroughOwnedWicJobAndTestPipe()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var fixture = await ThumbnailLiveFixture.OpenAsync(deadline.Token);
        // The real Core permits two concurrent logins; other NativeLive classes own their own sessions.
        // One isolated login covers both profiles without weakening production admission or adding retries.
        foreach (var profile in new[] { DerivedImageProfile.Thumbnail512, DerivedImageProfile.Preview1600 })
        {
            foreach (var name in new[] { "landscape.png", "transparent.png" })
            {
                var item = fixture.Page.Items.Single(item => item.Name == name);
                var response = await ThumbnailLiveSupport.ReadPipeAsync(profile == DerivedImageProfile.Thumbnail512 ? fixture.Endpoint : fixture.PreviewEndpoint, new ThumbnailRequest(31, fixture.Page.Epoch, item.Node), deadline.Token, profile);
                Assert.AreEqual(ThumbnailStatus.Ready, response.Status);
                Assert.IsNotNull(response.Image);
                Assert.IsLessThanOrEqualTo((uint)DerivedImageSpecification.For(profile).MaximumEdge, response.Image.Width);
                Assert.IsLessThanOrEqualTo((uint)DerivedImageSpecification.For(profile).MaximumEdge, response.Image.Height);
                Assert.AreEqual((long)response.Image.Width * response.Image.Height * 4, response.Image.Bytes.LongLength);
            }
        }
    }
}
