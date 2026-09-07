using System.Runtime.CompilerServices;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Domain;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class InitialScanBoundaryTests
{
    [TestMethod]
    public async Task ReplacingAYieldedDirectoryWithALinkAbortsTheInitialScan()
    {
        using var sandbox = new RepositorySandbox();
        var root = Directory.CreateDirectory(Path.Combine(sandbox.Root, "library")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
        var external = Directory.CreateDirectory(Path.Combine(sandbox.Root, "external")).FullName;
        await File.WriteAllTextAsync(Path.Combine(external, "private.bin"), "outside-library");
        var target = SystemReadOnlyBoundaryTests.Target(root);
        var sink = new TestObservationSink(failStaging: false, failInitialization: false);
        var journal = new TestScanJournal(failFinalization: false);
        var discovery = new ReplacingDiscovery(async () =>
        {
            Directory.Delete(child);
            await SandboxDirectoryLink.CreateAsync(sandbox, child, external);
        });
        var service = new InitialReadOnlyScanService(
            new TestTargetQuery(target),
            discovery,
            sink,
            journal,
            TimeProvider.System,
            new InitialScanLogger(NullLogger<InitialReadOnlyScanService>.Instance));

        var result = await service.ExecuteAsync(
            new InitialScanRequest(target.LibraryId, TimeSpan.FromSeconds(10), BatchSize: 1),
            CancellationToken.None);

        Assert.AreEqual(InitialScanStatus.DiscoveryFailed, result.Status);
        Assert.AreEqual("directory_reparse_point", result.FailureCode);
        Assert.AreEqual(1, result.ObservedEntries);
        Assert.AreEqual(0, result.CommittedEntries);
        Assert.IsTrue(sink.Session!.Aborted);
        Assert.IsFalse(sink.Session.Completed);
        Assert.AreEqual(ScanRunTerminalState.DiscoveryFailed, journal.Finished!.State);
        Assert.AreEqual("outside-library", await File.ReadAllTextAsync(Path.Combine(external, "private.bin")));
    }

    private sealed class ReplacingDiscovery(Func<Task> replaceDirectory) : IReadOnlyFileDiscovery
    {
        public async IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(
            LibraryScanTarget target,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var entry in new SystemReadOnlyFileDiscovery()
                .DiscoverAsync(target, cancellationToken))
            {
                yield return entry;
                await replaceDirectory();
            }
        }
    }
}
