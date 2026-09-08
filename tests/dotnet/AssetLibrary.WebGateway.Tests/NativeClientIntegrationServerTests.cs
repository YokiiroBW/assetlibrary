using System.Globalization;
using System.Text.Json;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class NativeClientIntegrationServerTests
{
    [TestMethod]
    public async Task ServeProductionCoreForNativeClientsUntilExplicitStopOrDeadline()
    {
        if (Environment.GetEnvironmentVariable("ASSETLIBRARY_NATIVE_CLIENT_REQUIRED") != "1")
        {
            Assert.Inconclusive("Native client temporary server was not requested; this is not execution evidence.");
        }

        var duration = int.Parse(Environment.GetEnvironmentVariable("ASSETLIBRARY_NATIVE_CLIENT_SECONDS") ?? "7200", CultureInfo.InvariantCulture);
        Assert.IsTrue(duration is >= 1 and <= 7200, "The native server must have a bounded lifetime.");
        var settings = TrialHostIntegrationSettings.Load();
        var assets = new NativeClientSampleAssets(settings.RuntimeRoot,
            Environment.GetEnvironmentVariable("ASSETLIBRARY_NATIVE_IMAGE_FIXTURES"));
        string stopReason;
        await using (var host = await TrialHostIntegrationFixture.CreateAsync(settings, TimeSpan.FromSeconds(duration).Add(TimeSpan.FromMinutes(5))))
        {
            var libraryId = await NativeClientFixtureSeed.PrepareAsync(host, assets);
            var expiry = DateTimeOffset.UtcNow.AddSeconds(duration);
            var stopFile = await NativeClientConnectionFiles.PublishAsync(settings, host, libraryId, assets.FileCount, expiry);
            while (!File.Exists(stopFile) && DateTimeOffset.UtcNow < expiry)
            {
                await Task.Delay(200);
            }

            stopReason = File.Exists(stopFile) ? "explicit_stop" : "lifetime_elapsed";
            assets.VerifyUnchanged();
        }

        await File.WriteAllTextAsync(Path.Combine(settings.Evidence, "host-cleanup.json"), JsonSerializer.Serialize(new
        {
            status = "disposed",
            stop_reason = stopReason,
            sample_file_count = assets.FileCount,
            sample_hashes_and_mtimes = "unchanged",
        }));
    }
}
