
namespace AssetLibrary.ReadCore.Tests;

/// <summary>
/// Proves the physical read boundary: work this run could not do is reported as an incomplete
/// preview, a deadline or cancellation says so, a stored preview stays comparable and stale previews
/// are detected instead of reused.
/// </summary>
[TestClass]
public sealed class DedupPlanTests
{
    [TestMethod]
    public async Task TheFileCeilingStopsTheScanAndIsNamed()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("many");
        for (var index = 0; index < 6; index++)
        {
            scenario.WriteText($"many/file-{index}.bin", "same-content");
        }

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request(
                [DedupAnalysisFactory.Source(root, "many")],
                new DedupAnalysisLimits(MaximumFiles: 4, MaximumBytes: 1_000_000, MaximumFileBytes: 4096)));

        Assert.AreEqual(4, plan.Statistics.ObservedEntries);
        Assert.AreEqual("file_ceiling_reached", plan.Summary.FailureCode);
        Assert.AreEqual(DedupAnalysisStatus.PartiallyAnalyzed, plan.Summary.Status);
        Assert.HasCount(1, plan.Summary.SourceFailures);
    }

    [TestMethod]
    public async Task AFilesizeAboveTheCeilingIsSkippedAndNeverHashed()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("oversize");
        scenario.WritePatterned("oversize/first.bin", 20_000, seed: 21);
        scenario.WritePatterned("oversize/second.bin", 20_000, seed: 21);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request(
                [DedupAnalysisFactory.Source(root, "oversize")],
                new DedupAnalysisLimits(MaximumFiles: 100, MaximumBytes: 1_000_000, MaximumFileBytes: 4096)));

        Assert.AreEqual(0, plan.Statistics.AnalyzedFiles);
        Assert.AreEqual(2, plan.Statistics.SkippedFiles);
        Assert.IsTrue(plan.Summary.ScanBoundsReached);
        Assert.IsTrue(plan.Summary.IncompleteReasons.Contains(DedupSkipReason.ExceedsBudget));
        Assert.IsTrue(plan.Items.All(item => item.SkipReason == DedupSkipReason.ExceedsBudget));
        Assert.IsTrue(plan.Items.All(item => item.Sha256 is null));
    }

    [TestMethod]
    public async Task AnExhaustedByteBudgetIsReportedAndNeverGuessed()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("payload");
        scenario.WritePatterned("payload/alpha.bin", 4_000, seed: 31);
        scenario.WritePatterned("payload/beta.bin", 4_000, seed: 31);
        scenario.WritePatterned("payload/gamma.bin", 4_000, seed: 31);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request(
                [DedupAnalysisFactory.Source(root, "payload")],
                new DedupAnalysisLimits(MaximumFiles: 100, MaximumBytes: 8_000, MaximumFileBytes: 4096)));

        // Two reads were afforded whole out of the 8000-byte allowance and the third could not be, so the
        // allowance was spent on the bytes that were really read and the run is explicitly short of its own
        // plan: the third file is refused rather than analysed, the scan cannot claim to have reached the
        // end, and the stated volume is what was really read and not the 12000 the lengths added up to.
        Assert.AreEqual(2, plan.Statistics.AnalyzedFiles);
        Assert.AreEqual(8_000L, plan.Statistics.ReadBytes);
        Assert.IsTrue(plan.Summary.ScanBoundsReached);
        Assert.AreEqual(1, plan.Statistics.FailedFiles);

        // What was verified still groups honestly: the preview understates rather than inventing.
        Assert.AreEqual(1, plan.Statistics.ByteDuplicateGroups);
        Assert.HasCount(2, plan.Groups.Single().MemberIdentities);
    }

    [TestMethod]
    public async Task ADeadlineProducesAnExplicitBoundaryInsteadOfAPartialVerdict()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("stalled");
        scenario.WriteText("stalled/one.bin", "content-one");
        scenario.WriteText("stalled/two.bin", "content-two");
        var discovery = new BlockingDiscovery(root);

        var plan = await DedupAnalysisFactory
            .Create(scenario, new RecordingContentReader(), discovery)
            .AnalyzeAsync(
                DedupAnalysisFactory.Request(
                    [DedupAnalysisFactory.Source(root, "stalled")],
                    timeout: TimeSpan.FromMilliseconds(120)),
                CancellationToken.None);

        Assert.AreEqual(DedupAnalysisStatus.TimedOut, plan.Summary.Status);
        Assert.IsTrue(plan.Summary.ScanBoundsReached);
        Assert.AreEqual(0, plan.Statistics.ObservedEntries);
        Assert.IsEmpty(plan.Groups);
    }

    [TestMethod]
    public async Task CancellationIsHonouredAndNeverReportedAsSuccess()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("cancelled");
        scenario.WriteText("cancelled/a.bin", "shared");
        scenario.WriteText("cancelled/b.bin", "shared");
        var analyzer = DedupAnalysisFactory.Create(
            scenario,
            new RecordingContentReader(),
            new BlockingDiscovery(root));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));

        await Assert.ThrowsAsync<OperationCanceledException>(() => analyzer.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "cancelled")]),
            cancellation.Token));
    }

    [TestMethod]
    public async Task AnEmptyRequestOrImpossibleCeilingIsRefusedBeforeAnyRead()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("invalid");
        scenario.WriteText("invalid/a.bin", "shared");
        var analyzer = DedupAnalysisFactory.Create(scenario);
        var source = DedupAnalysisFactory.Source(root, "invalid");

        await Assert.ThrowsAsync<ArgumentException>(() => analyzer.AnalyzeAsync(
            DedupAnalysisFactory.Request([]),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => analyzer.AnalyzeAsync(
            DedupAnalysisFactory.Request([source], timeout: TimeSpan.Zero),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => analyzer.AnalyzeAsync(
            DedupAnalysisFactory.Request(
                [source],
                new DedupAnalysisLimits(MaximumFiles: 0, MaximumBytes: 1, MaximumFileBytes: 1)),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => analyzer.AnalyzeAsync(
            DedupAnalysisFactory.Request(
                [source],
                new DedupAnalysisLimits(1, 1, 1, HashConcurrency: 64)),
            CancellationToken.None));
    }
}

/// <summary>
/// Proves that a preview is a durable, comparable statement: unchanged sources reproduce the same
/// plan digest, a stored preview round-trips through JSON, and any source change makes it stale.
/// </summary>
[TestClass]
public sealed class DedupRecountTests
{
    [TestMethod]
    public async Task UnchangedSourcesReproduceTheSamePlanDigest()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("stable");
        scenario.WriteText("stable/a.bin", "shared");
        scenario.WriteText("stable/nested/b.bin", "shared");
        scenario.WriteText("stable/other.bin", "differs");
        var source = DedupAnalysisFactory.Source(root, "stable");

        var first = await scenario.AnalyzeAsync(DedupAnalysisFactory.Request([source]));
        var second = await scenario.AnalyzeAsync(DedupAnalysisFactory.Request([source]));

        Assert.AreNotEqual(first.AnalysisId, second.AnalysisId);
        Assert.AreEqual(DedupContractText.PolicyVersion, first.PolicyVersion);
        Assert.AreEqual(3, first.Statistics.ObservedEntries);
        Assert.AreEqual(2, first.Statistics.AnalyzedFiles);
        Assert.AreEqual(first.PlanDigest, second.PlanDigest);
        Assert.AreEqual(DedupAnalysisFactory.PathList(first), DedupAnalysisFactory.PathList(second));
    }

    [TestMethod]
    public async Task ARecountOfUnchangedSourcesStillHonoursTheStoredPlan()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("recheck");
        foreach (var name in new[] { "a.bin", "b.bin", "c.bin" })
        {
            scenario.WriteText($"recheck/{name}", "shared");
        }

        var sources = new[] { DedupAnalysisFactory.Source(root, "recheck", DedupSourceRole.RegisteredLibrary) };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.AreEqual(DedupRecountStatus.Identical, recount.Status);
        Assert.IsTrue(recount.PlanStillCurrent);
        Assert.IsTrue(string.Equals(plan.PlanDigest, recount.PlanDigest, StringComparison.Ordinal));
        Assert.IsTrue(string.Equals(plan.PlanDigest, recount.CurrentPlanDigest, StringComparison.Ordinal));
        Assert.IsEmpty(recount.ChangedPaths);
        Assert.IsEmpty(recount.DisappearedPaths);
        Assert.IsEmpty(recount.NewPaths);
        Assert.IsEmpty(recount.Reasons);
    }

    [TestMethod]
    public async Task ContentChangedUnderAPreviewMakesItStale()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("drifted");
        foreach (var name in new[] { "a.bin", "b.bin", "c.bin" })
        {
            scenario.WriteText($"drifted/{name}", "shared");
        }

        var sources = new[] { DedupAnalysisFactory.Source(root, "drifted") };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));

        // Same length, different bytes: only the complete strong hash can reveal this change.
        var replacement = "mutant"u8.ToArray();
        scenario.Mutate("drifted/b.bin", replacement);
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.AreEqual(DedupRecountStatus.SourceChanged, recount.Status);
        Assert.IsFalse(recount.PlanStillCurrent);
        var reasons = recount.Reasons.ToArray();
        Assert.IsTrue(Array.Exists(reasons, reason => reason == DedupRecountReason.ContentChanged));
        Assert.IsTrue(Array.Exists(reasons, reason => reason != DedupRecountReason.MetadataChanged));
        Assert.AreEqual("b.bin", recount.ChangedPaths.Single().Value);
        Assert.AreNotEqual(recount.PlanDigest, recount.CurrentPlanDigest);
        Assert.IsNotNull(recount.CurrentPlan);
        Assert.AreEqual(1, recount.CurrentPlan.Statistics.ByteDuplicateGroups);
    }

    [TestMethod]
    public async Task ARemovedFileIsReportedAsDisappeared()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("shrinking");
        scenario.WriteText("shrinking/alpha.bin", "content-alpha");
        scenario.WriteText("shrinking/beta.bin", "content-betaa");
        scenario.WriteText("shrinking/gamma.bin", "content-gamma");
        var sources = new[] { DedupAnalysisFactory.Source(root, "shrinking") };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));
        Assert.AreEqual(3, plan.Statistics.AnalyzedFiles);
        scenario.Delete("shrinking/beta.bin");
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.AreEqual(DedupRecountStatus.Disappeared, recount.Status);
        Assert.IsFalse(recount.PlanStillCurrent);
        Assert.AreEqual("beta.bin", recount.DisappearedPaths.Single().Value);
        Assert.IsEmpty(recount.NewPaths);

        // The remaining files are still compared, and nothing about them changed.
        Assert.IsEmpty(recount.ChangedPaths);
    }

    [TestMethod]
    public async Task NewContentIsReportedAsNewRatherThanUnchanged()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("growing");
        scenario.WriteText("growing/only.bin", "content-only");
        scenario.WriteText("growing/second.bin", "content-seco");
        var sources = new[] { DedupAnalysisFactory.Source(root, "growing") };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));
        Assert.AreEqual(2, plan.Statistics.AnalyzedFiles);
        scenario.WriteText("growing/added.bin", "content-adde");
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.AreEqual(DedupRecountStatus.NewContent, recount.Status);
        Assert.IsTrue(recount.Reasons.Contains(DedupRecountReason.NewFileObserved));
        Assert.AreEqual("added.bin", recount.NewPaths.Single().Value);
        Assert.IsFalse(recount.PlanStillCurrent);
    }

    [TestMethod]
    public async Task ASourceRejectedOnRecountMakesTheStoredPreviewStale()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("revoked");
        scenario.WriteText("revoked/a.bin", "shared");
        scenario.WriteText("revoked/b.bin", "shared");
        var sources = new[] { DedupAnalysisFactory.Source(root, "revoked") };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));
        scenario.AddOutputRoot("revoked");
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.AreEqual(DedupRecountStatus.SourceChanged, recount.Status);
        Assert.IsTrue(recount.Reasons.Contains(DedupRecountReason.SourceRejected));
        Assert.IsFalse(recount.PlanStillCurrent);
    }
}
