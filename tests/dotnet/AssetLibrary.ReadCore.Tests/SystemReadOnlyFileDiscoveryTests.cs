using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Domain;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class SystemReadOnlyFileDiscoveryTests
{
    private static readonly string[] ExpectedDiscoveredPaths = ["photos", "photos/image.jpg"];

    [TestMethod]
    public async Task DiscoveryStreamsVisibleEntriesAndLeavesEverySandboxByteUnchanged()
    {
        using var sandbox = new RepositorySandbox();
        var photos = Directory.CreateDirectory(Path.Combine(sandbox.Root, "photos"));
        var ignored = Directory.CreateDirectory(Path.Combine(sandbox.Root, ".assetmeta"));
        await File.WriteAllTextAsync(Path.Combine(photos.FullName, "image.jpg"), "asset-content");
        await File.WriteAllTextAsync(Path.Combine(sandbox.Root, "upload.partial"), "temporary");
        await File.WriteAllTextAsync(Path.Combine(ignored.FullName, "metadata.json"), "private-sidecar");
        var before = sandbox.CaptureStrongSnapshot();
        var target = Target(sandbox.Root);
        var discovery = new SystemReadOnlyFileDiscovery();

        var entries = new List<DiscoveredEntry>();
        await foreach (var entry in discovery.DiscoverAsync(target, CancellationToken.None))
        {
            entries.Add(entry);
        }

        CollectionAssert.AreEquivalent(
            ExpectedDiscoveredPaths,
            entries.Select(entry => entry.RelativePath.Value).ToArray());
        Assert.AreEqual(AssetEntryKind.Directory, entries.Single(entry => entry.RelativePath.Value == "photos").Kind);
        Assert.AreEqual(13, entries.Single(entry => entry.RelativePath.Value == "photos/image.jpg").ContentLength);
        Assert.AreEqual(before, sandbox.CaptureStrongSnapshot());
    }

    [TestMethod]
    public async Task DiscoveryHonorsCancellationBeforeEnumerating()
    {
        using var sandbox = new RepositorySandbox();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var discovery = new SystemReadOnlyFileDiscovery();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in discovery.DiscoverAsync(Target(sandbox.Root), cancellation.Token))
            {
            }
        });
    }

    private static LibraryScanTarget Target(string path) =>
        new(
            LibraryId.New(),
            StorageSourceId.New(),
            new CanonicalLibraryRoot(
                Path.GetFullPath(path).Replace('\\', '/'),
                OperatingSystem.IsWindows()
                    ? RootPathComparison.CaseInsensitive
                    : RootPathComparison.CaseSensitive),
            StorageAvailability.Online);
}
