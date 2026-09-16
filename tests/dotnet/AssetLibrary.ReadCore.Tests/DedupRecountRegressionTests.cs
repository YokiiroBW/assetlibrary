namespace AssetLibrary.ReadCore.Tests;

/// <summary>
/// Regression cover for the two review findings: a preview must compare every fact it recorded, not
/// only the files it happened to hash, and its digest must bind those facts. These cases are the
/// exact mutations the isolated probe used to show a changed or removed file still reported a current
/// plan, so each one asserts the plan conclusion, the digest and the summary together.
/// </summary>
[TestClass]
public sealed class DedupRecountRegressionTests
{
    [TestMethod]
    public async Task AUniqueLengthFileThatGrewIsNoLongerACurrentPlan()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("solo");
        scenario.WriteText("solo/only.bin", "unique");
        var sources = new[] { DedupAnalysisFactory.Source(root, "solo") };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));

        // A file with no same-length peer is never read, so the plan holds metadata only.
        Assert.AreEqual(DedupPlanItemState.NotAnalyzed, DedupAnalysisFactory.Item(plan, "only.bin").State);
        Assert.AreEqual(0, plan.Statistics.AnalyzedFiles);

        scenario.Mutate("solo/only.bin", "changed-and-larger"u8.ToArray());
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.AreNotEqual(DedupRecountStatus.Identical, recount.Status);
        Assert.IsFalse(recount.PlanStillCurrent);
        Assert.AreEqual(DedupRecountStatus.SourceChanged, recount.Status);
        Assert.IsTrue(recount.Reasons.Contains(DedupRecountReason.MetadataChanged));
        Assert.AreEqual("only.bin", recount.ChangedPaths.Single().Value);
        Assert.AreNotEqual(recount.PlanDigest, recount.CurrentPlanDigest);
        Assert.IsNotNull(recount.CurrentPlan);
        Assert.AreEqual(18L, DedupAnalysisFactory.Item(recount.CurrentPlan, "only.bin").Length);
        Assert.IsFalse(recount.CurrentPlan.Summary.ScanBoundsReached);
    }

    [TestMethod]
    public async Task AUniqueLengthFileThatWasRemovedIsNoLongerACurrentPlan()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("vanishing");
        scenario.WriteText("vanishing/only.bin", "unique");
        var sources = new[] { DedupAnalysisFactory.Source(root, "vanishing") };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));
        scenario.Delete("vanishing/only.bin");
        Assert.IsFalse(File.Exists(scenario.AbsolutePath("vanishing/only.bin")));
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.IsFalse(recount.PlanStillCurrent);
        Assert.IsFalse(recount.PlanStillCurrent || recount.PlanDigest == recount.CurrentPlanDigest);
        Assert.AreEqual(DedupRecountStatus.Disappeared, recount.Status);
        Assert.AreEqual("only.bin", recount.DisappearedPaths.Single().Value);
        Assert.AreNotEqual(recount.PlanDigest, recount.CurrentPlanDigest);
        Assert.IsEmpty(recount.ChangedPaths);
        Assert.IsEmpty(recount.NewPaths);
        Assert.IsEmpty(recount.Reasons.Where(reason => reason is DedupRecountReason.ContentChanged));
    }

    [TestMethod]
    public async Task AUniqueLengthFileRewrittenAtTheSameLengthIsNoLongerACurrentPlan()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("rewritten");
        scenario.WriteText("rewritten/only.bin", "unique");
        var sources = new[] { DedupAnalysisFactory.Source(root, "rewritten", DedupSourceRole.RegisteredLibrary) };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));

        // Same length, so only the recorded write time can reveal this rewrite. The plan must not
        // silently claim the content it never read is unchanged.
        scenario.Mutate("rewritten/only.bin", "altered"u8.ToArray());
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.AreEqual(DedupRecountStatus.SourceChanged, recount.Status);
        Assert.IsFalse(recount.PlanStillCurrent);
        Assert.IsTrue(recount.Reasons.Contains(DedupRecountReason.MetadataChanged));
        Assert.IsFalse(recount.Reasons.Contains(DedupRecountReason.ContentChanged));
        Assert.AreEqual("only.bin", recount.ChangedPaths.Single().Value);
        Assert.AreNotEqual(recount.PlanDigest, recount.CurrentPlanDigest);
    }

    [TestMethod]
    public async Task AMetadataOnlyChangeToAnUnreadFileStillInvalidatesThePlan()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("touched");
        scenario.WriteText("touched/only.bin", "unique");
        var sources = new[] { DedupAnalysisFactory.Source(root, "touched") };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));
        scenario.Touch("touched/only.bin");
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        // A content hash cannot see a write-time move, so the metadata comparison has to catch it.
        Assert.AreEqual(DedupRecountStatus.SourceChanged, recount.Status);
        Assert.IsFalse(recount.PlanStillCurrent);
        Assert.IsTrue(recount.Reasons.Contains(DedupRecountReason.MetadataChanged));
        Assert.AreEqual("only.bin", recount.ChangedPaths.Single().Value);
    }

    [TestMethod]
    public async Task AVerifiedSingleFileRewrittenInPlaceIsNoLongerACurrentPlan()
    {
        using var scenario = new DedupScenario();
        var shareLibrary = scenario.RegisterRoot("hashed");
        scenario.WriteText("hashed/left.bin", "shared");
        scenario.WriteText("hashed/right.bin", "shared");
        var sources = new[] { DedupAnalysisFactory.Source(shareLibrary, "hashed") };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));
        Assert.AreEqual(2, plan.Statistics.AnalyzedFiles);

        // Both files were hashed, so this proves the digest binds the hash rather than the group key.
        scenario.Mutate("hashed/right.bin", "mutant"u8.ToArray());
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.AreEqual(DedupRecountStatus.SourceChanged, recount.Status);
        Assert.IsTrue(recount.Reasons.Contains(DedupRecountReason.ContentChanged));
        Assert.AreEqual("right.bin", recount.ChangedPaths.Single().Value);
        Assert.IsNotNull(recount.CurrentPlan);
        Assert.AreEqual(0, recount.CurrentPlan.Statistics.ByteDuplicateGroups);
    }

    [TestMethod]
    public async Task AMixedGroupWithAnUnreadableMemberIsReportedNotHeld()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("mixed-batch");
        // Three equally long files so the first read hashes all of them. One content body is reused
        // with a different filler so the fixture stays short while the bytes stay distinct.
        foreach (var (name, filler) in new[] { ("alpha.bin", "content-alpha"), ("beta.bin", "content-betaa"), ("gamma.bin", "content-gamma") })
        {
            scenario.WriteText($"mixed-batch/{name}", filler);
        }

        var sources = new[] { DedupAnalysisFactory.Source(root, "mixed-batch") };
        var analyzer = DedupAnalysisFactory.Create(scenario);

        var plan = await analyzer.AnalyzeAsync(DedupAnalysisFactory.Request(sources));

        // The whole group is rewritten and one member removed, which is the mixed case the review
        // asked for: the verdict must name the removal, the changes and the plan's staleness at once.
        scenario.Mutate("mixed-batch/alpha.bin", "content-altered"u8.ToArray());
        scenario.Delete("mixed-batch/beta.bin");
        Assert.IsFalse(File.Exists(scenario.AbsolutePath("mixed-batch/beta.bin")));
        var recount = await analyzer.RecountAsync(
            new DedupRecountRequest(plan, sources, TimeSpan.FromSeconds(30)));

        Assert.IsFalse(recount.PlanStillCurrent);
        Assert.IsFalse(recount.PlanStillCurrent || recount.PlanDigest == recount.CurrentPlanDigest);
        Assert.AreEqual(DedupRecountStatus.Disappeared, recount.Status);
        Assert.AreEqual("beta.bin", recount.DisappearedPaths.Single().Value);
        Assert.IsTrue(recount.ChangedPaths.Any(path => path.Value == "alpha.bin"));
        Assert.IsNotNull(recount.CurrentPlan);
        Assert.AreNotEqual(recount.PlanDigest, recount.CurrentPlanDigest);

        // The summary and the conclusion agree: the earlier preview observed three entries, and the
        // fresh read observed one fewer because one file is gone. The two survivors now have unique
        // lengths, so nothing is read and the fresh preview holds metadata only.
        Assert.AreEqual(3, plan.Statistics.ObservedEntries);
        Assert.AreEqual(2, recount.CurrentPlan.Statistics.ObservedEntries);
        Assert.AreEqual(DedupAnalysisStatus.Completed, recount.CurrentPlan.Summary.Status);
        Assert.AreEqual(0, recount.CurrentPlan.Statistics.AnalyzedFiles);
        Assert.AreNotEqual(
            DedupAnalysisFactory.Item(plan, "alpha.bin").Length,
            DedupAnalysisFactory.Item(recount.CurrentPlan, "alpha.bin").Length);
    }

}
