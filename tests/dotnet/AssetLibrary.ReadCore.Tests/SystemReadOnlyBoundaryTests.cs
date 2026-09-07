using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Domain;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class SystemReadOnlyBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RootProbeRejectsLinksInTheRootOrItsAncestors(bool linkedAncestor)
    {
        using var sandbox = new RepositorySandbox();
        var root = await LinkedRootAsync(sandbox, linkedAncestor);

        var result = await new SystemLibraryRootProbe().ProbeAsync(
            root,
            Comparison,
            CancellationToken.None);

        Assert.AreEqual(LibraryRootProbeStatus.Inaccessible, result.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DiscoveryRejectsLinkedRootsBeforeObservingAnyExternalEntry(bool linkedAncestor)
    {
        using var sandbox = new RepositorySandbox();
        var root = await LinkedRootAsync(sandbox, linkedAncestor);
        var observed = 0;

        var failure = await Assert.ThrowsExactlyAsync<FileDiscoveryException>(async () =>
        {
            await foreach (var _ in new SystemReadOnlyFileDiscovery()
                .DiscoverAsync(Target(root), CancellationToken.None))
            {
                observed++;
            }
        });

        Assert.AreEqual("directory_reparse_point", failure.FailureCode);
        Assert.AreEqual(0, observed);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ReplacingTheEnumeratedRootIsDetectedBeforeReadingOrCompleting(int entryCount)
    {
        using var sandbox = new RepositorySandbox();
        var root = Directory.CreateDirectory(Path.Combine(sandbox.Root, "library")).FullName;
        var external = Directory.CreateDirectory(Path.Combine(sandbox.Root, "external")).FullName;
        for (var index = 0; index < entryCount; index++)
        {
            var name = $"item-{index}.bin";
            await File.WriteAllTextAsync(Path.Combine(root, name), "inside");
            await File.WriteAllTextAsync(Path.Combine(external, name), "outside-library");
        }

        await using var entries = new SystemReadOnlyFileDiscovery()
            .DiscoverAsync(Target(root), CancellationToken.None).GetAsyncEnumerator();
        Assert.IsTrue(await entries.MoveNextAsync());
        Directory.Move(root, Path.Combine(sandbox.Root, "original"));
        await SandboxDirectoryLink.CreateAsync(sandbox, root, external);

        var failure = await Assert.ThrowsExactlyAsync<FileDiscoveryException>(async () =>
            _ = await entries.MoveNextAsync());

        Assert.AreEqual("directory_reparse_point", failure.FailureCode);
    }

    [TestMethod]
    public async Task AnUnchangedChildLinkIsObservedWithoutRecursingIntoItsTarget()
    {
        using var sandbox = new RepositorySandbox();
        var root = Directory.CreateDirectory(Path.Combine(sandbox.Root, "library")).FullName;
        var external = Directory.CreateDirectory(Path.Combine(sandbox.Root, "external")).FullName;
        await File.WriteAllTextAsync(Path.Combine(external, "private.bin"), "outside-library");
        await SandboxDirectoryLink.CreateAsync(sandbox, Path.Combine(root, "link"), external);
        var entries = new List<DiscoveredEntry>();

        await foreach (var entry in new SystemReadOnlyFileDiscovery()
            .DiscoverAsync(Target(root), CancellationToken.None))
        {
            entries.Add(entry);
        }

        Assert.HasCount(1, entries);
        Assert.AreEqual("link", entries[0].RelativePath.Value);
        Assert.AreEqual(AssetEntryKind.ReparseDirectory, entries[0].Kind);
    }

    internal static RootPathComparison Comparison => OperatingSystem.IsWindows()
        ? RootPathComparison.CaseInsensitive
        : RootPathComparison.CaseSensitive;

    internal static LibraryScanTarget Target(string path) => new(
        LibraryId.New(),
        StorageSourceId.New(),
        new CanonicalLibraryRoot(Path.GetFullPath(path), Comparison),
        StorageAvailability.Online);

    private static async Task<string> LinkedRootAsync(RepositorySandbox sandbox, bool linkedAncestor)
    {
        var external = Directory.CreateDirectory(Path.Combine(sandbox.Root, "external")).FullName;
        var physicalRoot = linkedAncestor
            ? Directory.CreateDirectory(Path.Combine(external, "library")).FullName
            : external;
        await File.WriteAllTextAsync(Path.Combine(physicalRoot, "private.bin"), "outside-library");
        var link = Path.Combine(sandbox.Root, "alias");
        await SandboxDirectoryLink.CreateAsync(sandbox, link, external);
        return linkedAncestor ? Path.Combine(link, "library") : link;
    }

}
