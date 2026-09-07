using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;
using AssetLibrary.Modules.ScanReconciliation.Application;
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PosixBackslashNamesAreRejectedWithoutChangingPhysicalIdentity(bool directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Literal backslash filenames require a POSIX filesystem.");
        }

        using var sandbox = new RepositorySandbox();
        var path = Path.Combine(sandbox.Root, "a\\b");
        if (directory)
        {
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "content.txt"), "original");
        }
        else
        {
            await File.WriteAllTextAsync(path, "original");
        }

        var before = sandbox.CaptureStrongSnapshot();
        var probe = await new SystemLibraryRootProbe().ProbeAsync(path, RootPathComparison.CaseSensitive, CancellationToken.None);
        Assert.AreEqual(LibraryRootProbeStatus.Inaccessible, probe.Status);
        var observed = 0;
        var error = await Assert.ThrowsExactlyAsync<FileDiscoveryException>(async () =>
        {
            await foreach (var _ in new SystemReadOnlyFileDiscovery().DiscoverAsync(Target(sandbox.Root), CancellationToken.None))
            {
                observed++;
            }
        });
        Assert.AreEqual("entry_path_unsupported", error.FailureCode);
        Assert.AreEqual(0, observed);
        Assert.AreEqual(before, sandbox.CaptureStrongSnapshot());
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
