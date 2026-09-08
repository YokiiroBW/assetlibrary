using System.Security.Cryptography;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Infrastructure;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class ImageSourceBrokerTests
{
    [TestMethod]
    public async Task RealHostSourceChildStreamsVerifiedBytesAndReleasesTheOriginalHandle()
    {
        var executable = Environment.GetEnvironmentVariable("ASSETLIBRARY_TEST_DOTNET");
        var host = Environment.GetEnvironmentVariable("ASSETLIBRARY_TEST_HOST_DLL");
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(host))
        {
            Assert.Inconclusive("The source-broker runner must provide the real built Host and pinned dotnet executable.");
            return;
        }
        using var fixture = new ImageSourceFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var reader = new ProcessImageSourceReader(new ReadOnlyWorkerProcessOptions(executable, [host]));
        var target = new LibraryScanTarget(LibraryId.New(), StorageSourceId.New(), fixture.CanonicalRoot, StorageAvailability.Online);
        var entry = new AssetObservation(StableEntryId.New(), new RelativeAssetPath("中文/source.bin"),
            AssetEntryKind.File, fixture.Bytes.Length, fixture.Modified);
        await using (var source = await reader.OpenAsync(target, entry, deadline.Token))
        {
            Assert.AreEqual((long)fixture.Bytes.Length, source.Length);
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(fixture.Bytes)), source.ContentHash);
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy, deadline.Token);
            CollectionAssert.AreEqual(fixture.Bytes, copy.ToArray());
            await source.VerifyAsync(deadline.Token);
        }
        using var reopened = new FileStream(fixture.Asset, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.AreEqual((long)fixture.Bytes.Length, reopened.Length);
        Assert.AreEqual(fixture.Modified, new DateTimeOffset(File.GetLastWriteTimeUtc(fixture.Asset)));
    }
}
