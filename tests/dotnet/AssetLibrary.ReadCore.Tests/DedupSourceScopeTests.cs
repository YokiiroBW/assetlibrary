
namespace AssetLibrary.ReadCore.Tests;

/// <summary>
/// Proves which directories may be analysed at all: only this installation's own registered roots
/// and explicit inbound staging, never a re-registered managed library, a nested staging directory
/// or the publication output area this product writes itself.
/// </summary>
[TestClass]
public sealed class DedupSourceScopeTests
{
    [TestMethod]
    public async Task ANestedStagingDirectoryInsideALibraryIsRefused()
    {
        using var scenario = new DedupScenario();
        var library = scenario.RegisterRoot("library");
        scenario.WriteText("library/kept.bin", "content");
        scenario.WriteText("library/staging/incoming.bin", "content");
        var inbound = scenario.UnregisteredRoot("library/staging");
        var recorder = new RecordingContentReader();

        var plan = await DedupAnalysisFactory.Create(scenario, recorder).AnalyzeAsync(
            DedupAnalysisFactory.Request(
            [
                DedupAnalysisFactory.Source(inbound, "incoming"),
                DedupAnalysisFactory.Source(library, "library-in-place", DedupSourceRole.RegisteredLibrary),
            ]),
            CancellationToken.None);

        // Only the nested staging directory is refused; reading the library root in place is normal.
        var rejected = plan.RejectedSources.Single();
        Assert.AreEqual(DedupSourceRejection.ManagedLibraryOverlap, rejected.Rejection);
        Assert.AreEqual(inbound.Value, rejected.Root);
        Assert.HasCount(1, plan.AcceptedSources);
        Assert.AreEqual(library.Value, plan.AcceptedSources[0].Root);
        Assert.IsFalse(recorder.Requests.Any(request => request.Root.Value == inbound.Value));
    }

    [TestMethod]
    public async Task AStagingAreaInsideTheOutputRootIsRefusedAsBackflow()
    {
        using var scenario = new DedupScenario();
        scenario.AddOutputRoot("published");
        scenario.CreateDirectory("published/staging");
        scenario.WriteText("published/managed.bin", "archive");
        scenario.WriteText("published/staging/incoming.bin", "archive");
        var inbound = scenario.UnregisteredRoot("published/staging");

        var plan = await DedupAnalysisFactory.Create(scenario).AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(inbound, "incoming")]));

        Assert.AreEqual(DedupAnalysisStatus.SourceRejected, plan.Summary.Status);
        Assert.AreEqual(DedupSourceRejection.OutputBackflow, plan.RejectedSources.Single().Rejection);
        Assert.AreEqual(inbound.Value, plan.RejectedSources[0].Root);
        Assert.AreEqual(0, plan.Statistics.ObservedEntries);
    }

    [TestMethod]
    public async Task ARootBesideTheOutputAreaIsStillAllowed()
    {
        using var scenario = new DedupScenario();
        scenario.AddOutputRoot("published");
        scenario.WriteText("published/managed.bin", "archive");
        scenario.CreateDirectory("holding");
        var sibling = scenario.UnregisteredRoot("holding");

        var plan = await DedupAnalysisFactory.Create(scenario).AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(sibling, "holding")]));

        // Being a sibling is not backflow; only living inside (or containing) the output is.
        Assert.AreEqual(DedupAnalysisStatus.Empty, plan.Summary.Status);
        Assert.IsEmpty(plan.RejectedSources);
        Assert.HasCount(1, plan.AcceptedSources);
    }

    [TestMethod]
    public async Task TheSamePhysicalDirectoryCannotBeRegisteredTwice()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("shared-root");
        scenario.WriteText("shared-root/one.bin", "content");

        var plan = await DedupAnalysisFactory.Create(scenario).AnalyzeAsync(
            DedupAnalysisFactory.Request(
            [
                DedupAnalysisFactory.Source(root, "first"),
                DedupAnalysisFactory.Source(root, "second"),
                DedupAnalysisFactory.Source(scenario.UnregisteredRoot("shared-root/nested"), "child"),
            ]));

        Assert.HasCount(1, plan.AcceptedSources);
        Assert.HasCount(2, plan.RejectedSources);
        Assert.IsTrue(plan.RejectedSources.All(
            rejected => rejected.Rejection == DedupSourceRejection.DuplicateSourceRegistration));
    }

    [TestMethod]
    public async Task AnUnknownRegisteredLibraryIsRefusedInsteadOfBeingRead()
    {
        using var scenario = new DedupScenario();
        scenario.WriteText("unknown/data.bin", "content");
        var unrelated = scenario.UnregisteredRoot("unknown");

        var plan = await DedupAnalysisFactory.Create(scenario).AnalyzeAsync(
            DedupAnalysisFactory.Request(
            [
                DedupAnalysisFactory.Source(
                    unrelated,
                    "unknown-library",
                    DedupSourceRole.RegisteredLibrary,
                    LibraryId.New()),
            ]));

        Assert.AreEqual(DedupAnalysisStatus.SourceRejected, plan.Summary.Status);
        Assert.AreEqual(DedupSourceRejection.RegisteredLibraryUnknown, plan.RejectedSources.Single().Rejection);
        Assert.AreEqual(0, plan.Statistics.AnalyzedFiles);
    }

    [TestMethod]
    public async Task AFileSystemRootIsNotAnAcceptableSource()
    {
        using var scenario = new DedupScenario();
        var volume = DedupScenario.CanonicalRoot(Path.GetPathRoot(Path.GetFullPath(scenario.Root))!);

        var plan = await DedupAnalysisFactory.Create(scenario).AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(volume, "whole-volume")]));

        Assert.AreEqual(DedupAnalysisStatus.SourceRejected, plan.Summary.Status);
        Assert.AreEqual(DedupSourceRejection.SourceRootInvalid, plan.RejectedSources.Single().Rejection);
    }

    [TestMethod]
    public async Task AnOfflineSourceIsReportedAsUnavailableInsteadOfEmpty()
    {
        using var scenario = new DedupScenario();
        var missing = scenario.UnregisteredRoot("unmounted");

        var plan = await DedupAnalysisFactory.Create(scenario).AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(missing, "offline-share")]));

        Assert.AreEqual(DedupAnalysisStatus.SourceRejected, plan.Summary.Status);
        Assert.AreEqual(DedupSourceRejection.SourceUnavailable, plan.RejectedSources.Single().Rejection);

        // An unreachable share is a scan boundary, never evidence that its files were removed.
        Assert.AreEqual(0, plan.Statistics.ObservedEntries);
        Assert.IsEmpty(plan.Items);
    }

    [TestMethod]
    public async Task AnEmptySourceIsReportedAsEmptyNotAsFullyAnalysed()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("barren");
        scenario.CreateDirectory("barren/nothing-here");

        var plan = await DedupAnalysisFactory.Create(scenario).AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "barren")]));

        Assert.AreEqual(DedupAnalysisStatus.Empty, plan.Summary.Status);
        Assert.AreEqual(0, plan.Statistics.ObservedEntries);
        Assert.IsEmpty(plan.Groups);
        Assert.IsEmpty(plan.RejectedSources);
    }

    [TestMethod]
    public async Task AnAbortedTraversalIsReportedAsAnIncompleteScan()
    {
        using var scenario = new DedupScenario();
        scenario.WriteText("walkable/one.bin", "alpha");
        scenario.WriteText("walkable/two.bin", "alpha");
        var walkable = scenario.UnregisteredRoot("walkable");
        scenario.CreateDirectory("blocked");
        var blocked = scenario.UnregisteredRoot("blocked");
        var discovery = new FailingSecondRootDiscovery(blocked, "directory_access_denied");

        var plan = await DedupAnalysisFactory
            .Create(scenario, new RecordingContentReader(), discovery)
            .AnalyzeAsync(
                DedupAnalysisFactory.Request(
                [
                    DedupAnalysisFactory.Source(walkable, "walkable"),
                    DedupAnalysisFactory.Source(blocked, "blocked"),
                ]));

        Assert.AreEqual("directory_access_denied", plan.Summary.FailureCode);
        Assert.HasCount(1, plan.Summary.SourceFailures);
        Assert.AreEqual(DedupAnalysisStatus.PartiallyAnalyzed, plan.Summary.Status);

        // The readable root is still fully analysed; the failure never becomes "no files here".
        Assert.AreEqual(1, plan.Statistics.ByteDuplicateGroups);
    }
}

/// <summary>
/// Proves the physical read boundary: a path that leaves its allowed root is refused, a reparse
/// point cannot redirect a read, a missing file is named rather than guessed, and an invalid
/// request never reaches the reader.
/// </summary>
[TestClass]
public sealed class DedupPathBoundaryTests
{
    [TestMethod]
    public async Task ARelativeNameBesideTheRootIsReportedAsMissingNotAsRead()
    {
        using var scenario = new DedupScenario();
        scenario.CreateDirectory("bounded");
        var root = scenario.UnregisteredRoot("bounded");
        scenario.WriteText("bounded/legit.bin", "content that must never leave");
        var discovery = new EscapingDiscovery(root, "sibling/absent.bin");
        var recorder = new RecordingContentReader();

        var plan = await DedupAnalysisFactory.Create(scenario, recorder, discovery).AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "bounded")]),
            CancellationToken.None);

        var absent = DedupAnalysisFactory.Item(plan, "sibling/absent.bin");
        Assert.AreEqual(DedupReadFailure.Missing, absent.Failure);
        Assert.AreEqual(DedupPlanItemState.Unreadable, absent.State);
        Assert.IsNull(absent.Sha256);
        Assert.AreEqual("sibling/absent.bin", plan.Summary.UnreadablePaths.Single());

        // The legitimate file in the same batch was still read and hashed.
        Assert.AreEqual(1, plan.Statistics.AnalyzedFiles);
        Assert.AreEqual(1, plan.Statistics.FailedFiles);
        Assert.IsNotNull(recorder.Results.Single(result => result.RelativePath.Value == "legit.bin").Sha256);
    }

    [TestMethod]
    public async Task AFileThatVanishedMidAnalysisIsNamedAndTheRestStaysUsable()
    {
        using var scenario = new DedupScenario();
        scenario.CreateDirectory("volatile");
        var root = scenario.UnregisteredRoot("volatile");
        scenario.WritePatterned("volatile/present.bin", 2048, seed: 41);
        scenario.WritePatterned("volatile/vanishing.bin", 2048, seed: 41);
        var discovery = new VanishingFileDiscovery(scenario, "volatile/vanishing.bin");

        var plan = await DedupAnalysisFactory
            .Create(scenario, new RecordingContentReader(), discovery)
            .AnalyzeAsync(
                DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "volatile")]),
                CancellationToken.None);

        var vanished = DedupAnalysisFactory.Item(plan, "vanishing.bin");
        Assert.AreEqual(DedupPlanItemState.Unreadable, vanished.State);
        Assert.AreEqual(DedupReadFailure.Missing, vanished.Failure);
        Assert.AreEqual(1, plan.Statistics.AnalyzedFiles);
        Assert.AreEqual(1, plan.Statistics.FailedFiles);
        Assert.AreEqual(DedupAnalysisStatus.PartiallyAnalyzed, plan.Summary.Status);
    }

    [TestMethod]
    public async Task AJunctionInTheAncestryRefusesTheWholeWalk()
    {
        using var scenario = new DedupScenario();
        scenario.CreateDirectory("real-parent/root");
        scenario.WriteText("real-parent/root/data.bin", "content behind a junction");
        var link = scenario.TrackJunction(Path.Combine(scenario.Root, "linked-parent"));
        DedupScenario.CreateDirectoryJunction(link, scenario.AbsolutePath("real-parent"));
        var junctionRoot = DedupScenario.CanonicalRoot(Path.Combine(link, "root"));

        var plan = await DedupAnalysisFactory
            .Create(scenario, new RecordingContentReader())
            .AnalyzeAsync(
                DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(junctionRoot, "junction-root")]));

        // A source that cannot be walked is an incomplete scan, never an empty library.
        Assert.AreEqual(DedupAnalysisStatus.PartiallyAnalyzed, plan.Summary.Status);
        Assert.HasCount(1, plan.Summary.SourceFailures);
        Assert.AreEqual("directory_reparse_point", plan.Summary.FailureCode);
        Assert.AreEqual(0, plan.Statistics.AnalyzedFiles);
        Assert.IsEmpty(plan.Groups);
    }
}
