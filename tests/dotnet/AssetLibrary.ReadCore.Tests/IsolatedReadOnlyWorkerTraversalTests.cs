using System.Text.Json;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class IsolatedReadOnlyWorkerTraversalTests
{
    [TestMethod]
    public async Task WideTreesDescendImmediatelyWithoutAccumulatingSiblingDirectories()
    {
        using var sandbox = new RepositorySandbox();
        for (var index = 0; index < 1000; index++)
        {
            var directory = Directory.CreateDirectory(Path.Combine(sandbox.Root, $"folder-{index}"));
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "file.bin"), "x");
        }

        await using var entries = new SystemReadOnlyFileDiscovery().DiscoverAsync(Target(sandbox.Root), CancellationToken.None).GetAsyncEnumerator();
        Assert.IsTrue(await entries.MoveNextAsync());
        var parent = entries.Current.RelativePath.Value;
        Assert.IsTrue(await entries.MoveNextAsync());
        Assert.AreEqual(parent + "/file.bin", entries.Current.RelativePath.Value);
    }

    [TestMethod]
    public async Task NativeProcessTerminatesOnCancellationAndLeavesItsSourceUnchanged()
    {
        var options = ReadOnlyTrialFixture.WorkerOptions();
        using var sandbox = new RepositorySandbox();
        await File.WriteAllTextAsync(Path.Combine(sandbox.Root, "file.bin"), "x");
        var before = sandbox.CaptureStrongSnapshot();
        using var cancellation = new CancellationTokenSource();
        var discovery = new ProcessReadOnlyFileDiscovery(options);
        await using var entries = discovery.DiscoverAsync(Target(sandbox.Root), cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        Assert.IsTrue(await entries.MoveNextAsync());
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await entries.MoveNextAsync());
        Assert.AreEqual(before, sandbox.CaptureStrongSnapshot());
    }

    internal static LibraryScanTarget Target(string path) => new(LibraryId.New(), StorageSourceId.New(),
        new CanonicalLibraryRoot(path, OperatingSystem.IsWindows() ? RootPathComparison.CaseInsensitive : RootPathComparison.CaseSensitive), StorageAvailability.Online);
}
