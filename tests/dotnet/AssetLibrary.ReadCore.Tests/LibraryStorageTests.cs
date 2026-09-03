using System.Runtime.CompilerServices;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Domain;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class LibraryStorageTests
{
    [TestMethod]
    [DataRow("C:\\Assets\\Photos\\", "C:/Assets/Photos")]
    [DataRow("/srv/assets/", "/srv/assets")]
    [DataRow("//nas/share/assets/", "//nas/share/assets")]
    public void CanonicalRootNormalizesSeparatorsAndEndingSlash(string input, string expected)
    {
        var root = new CanonicalLibraryRoot(input, RootPathComparison.CaseSensitive);

        Assert.AreEqual(expected, root.Value);
    }

    [TestMethod]
    [DataRow("relative/path")]
    [DataRow("/srv/../secret")]
    [DataRow("/srv//assets")]
    [DataRow("//nas")]
    [DataRow("//")]
    [DataRow("///")]
    [DataRow("C://")]
    public void CanonicalRootRejectsNonCanonicalInput(string input)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new CanonicalLibraryRoot(input, RootPathComparison.CaseSensitive));
    }

    [TestMethod]
    public void OverlapPolicyRejectsSameParentAndChildRootsButNotPrefixLookalikes()
    {
        var source = StorageSourceId.New();
        var existing = Registered(source, "/assets/photos");

        Assert.AreEqual(
            LibraryRootOverlapKind.SameRoot,
            LibraryRootOverlapPolicy.Find(Registered(source, "/assets/photos"), existing)?.Kind);
        Assert.AreEqual(
            LibraryRootOverlapKind.CandidateContainsExisting,
            LibraryRootOverlapPolicy.Find(Registered(source, "/assets"), existing)?.Kind);
        Assert.AreEqual(
            LibraryRootOverlapKind.ExistingContainsCandidate,
            LibraryRootOverlapPolicy.Find(Registered(source, "/assets/photos/raw"), existing)?.Kind);
        Assert.IsNull(LibraryRootOverlapPolicy.Find(Registered(source, "/assets/photoshop"), existing));
    }

    [TestMethod]
    public void OverlapPolicyUsesStoragePathCaseRulesAndSeparatesStorageSources()
    {
        var source = StorageSourceId.New();
        var insensitive = Registered(source, "C:/Assets", RootPathComparison.CaseInsensitive);

        Assert.IsNotNull(LibraryRootOverlapPolicy.Find(Registered(source, "c:/assets/raw"), insensitive));
        Assert.IsNull(LibraryRootOverlapPolicy.Find(
            Registered(StorageSourceId.New(), "c:/assets/raw"),
            insensitive));
    }

    [TestMethod]
    public void OverlapPolicyPreservesCaseDistinctionsForCaseSensitiveStorage()
    {
        var source = StorageSourceId.New();
        var existing = Registered(source, "/Assets", RootPathComparison.CaseSensitive);

        Assert.IsNull(LibraryRootOverlapPolicy.Find(
            Registered(source, "/assets/raw", RootPathComparison.CaseSensitive),
            existing));
    }

    [TestMethod]
    public async Task ValidationServiceReturnsOverlapWithoutRegisteringAnything()
    {
        var source = StorageSourceId.New();
        var existing = Registered(source, "/assets/photos");
        var probe = new StubRootProbe(
            new LibraryRootProbeResult(
                LibraryRootProbeStatus.Available,
                new CanonicalLibraryRoot("/assets", RootPathComparison.CaseSensitive)));
        var service = new LibraryRootValidationService(probe, new StubRootQuery([existing]));

        var result = await service.ValidateAsync(
            LibraryId.New(),
            source,
            "/assets",
            RootPathComparison.CaseSensitive,
            CancellationToken.None);

        Assert.AreEqual(LibraryRootValidationStatus.Overlapping, result.Status);
        Assert.AreEqual(existing.LibraryId, result.Overlap?.ExistingLibraryId);
    }

    [TestMethod]
    public async Task ValidationServiceDoesNotTreatTheCandidateLibraryAsItsOwnConflict()
    {
        var source = StorageSourceId.New();
        var candidateId = LibraryId.New();
        var existing = new RegisteredLibraryRoot(
            candidateId,
            source,
            new CanonicalLibraryRoot("/assets", RootPathComparison.CaseSensitive));
        var probe = new StubRootProbe(
            new LibraryRootProbeResult(LibraryRootProbeStatus.Available, existing.Root));
        var service = new LibraryRootValidationService(probe, new StubRootQuery([existing]));

        var result = await service.ValidateAsync(
            candidateId,
            source,
            "/assets",
            RootPathComparison.CaseSensitive,
            CancellationToken.None);

        Assert.AreEqual(LibraryRootValidationStatus.Valid, result.Status);
        Assert.IsNull(result.Overlap);
    }

    [TestMethod]
    public async Task SystemProbeReadsOnlyTaskSandboxAndReportsMissingPath()
    {
        using var sandbox = new RepositorySandbox();
        var file = Path.Combine(sandbox.Root, "asset.txt");
        await File.WriteAllTextAsync(file, "unchanged");
        var before = sandbox.CaptureStrongSnapshot();
        var probe = new SystemLibraryRootProbe();

        var available = await probe.ProbeAsync(
            sandbox.Root,
            RootPathComparison.CaseInsensitive,
            CancellationToken.None);
        var missing = await probe.ProbeAsync(
            Path.Combine(sandbox.Root, "missing"),
            RootPathComparison.CaseInsensitive,
            CancellationToken.None);

        Assert.AreEqual(LibraryRootProbeStatus.Available, available.Status);
        Assert.AreEqual(LibraryRootProbeStatus.Missing, missing.Status);
        Assert.AreEqual(before, sandbox.CaptureStrongSnapshot());
    }

    private static RegisteredLibraryRoot Registered(
        StorageSourceId source,
        string root,
        RootPathComparison comparison = RootPathComparison.CaseSensitive) =>
        new(LibraryId.New(), source, new CanonicalLibraryRoot(root, comparison));

    private sealed class StubRootProbe(LibraryRootProbeResult result) : ILibraryRootProbe
    {
        public ValueTask<LibraryRootProbeResult> ProbeAsync(
            string path,
            RootPathComparison comparison,
            CancellationToken cancellationToken) => ValueTask.FromResult(result);
    }

    private sealed class StubRootQuery(IReadOnlyList<RegisteredLibraryRoot> roots)
        : IRegisteredLibraryRootQuery
    {
        public async IAsyncEnumerable<RegisteredLibraryRoot> ListAsync(
            StorageSourceId storageSourceId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return root;
            }
        }
    }
}
