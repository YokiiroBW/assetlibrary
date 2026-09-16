
namespace AssetLibrary.ReadCore.Tests;

/// <summary>
/// Proves the read-only evidence contract: only a complete strong hash may mark a byte duplicate,
/// and equal names, equal sizes, animations, re-encodes, sidecars and reparse points must all stay
/// separate assets that nobody is invited to remove.
/// </summary>
[TestClass]
public sealed class DedupEvidenceTests
{
    [TestMethod]
    public async Task TwoDirectoriesWithEqualBytesFormExactlyOneDuplicateGroup()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("inbound");
        scenario.WriteText("inbound/a/cat.png", "same-bytes");
        scenario.WriteText("inbound/b/cat-copy.png", "same-bytes");
        scenario.WriteText("inbound/c/unique.png", "only-bytes");

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "inbound")]));

        Assert.AreEqual(DedupAnalysisStatus.Completed, plan.Summary.Status);
        Assert.AreEqual(1, plan.Statistics.ByteDuplicateGroups);
        Assert.AreEqual(2, plan.Statistics.ByteDuplicateFiles);
        Assert.AreEqual(3, plan.Statistics.AnalyzedFiles);
        Assert.HasCount(1, plan.Groups);
        Assert.AreEqual(AssetRelation.ByteDuplicate, plan.Groups[0].Evidence);
        Assert.IsFalse(plan.Groups[0].IdentityMergeProposed);

        var first = DedupAnalysisFactory.Item(plan, "a/cat.png");
        var second = DedupAnalysisFactory.Item(plan, "b/cat-copy.png");
        var unique = DedupAnalysisFactory.Item(plan, "c/unique.png");
        Assert.AreEqual(first.GroupKey, second.GroupKey);
        Assert.AreEqual(DedupCategory.ByteDuplicateGroup, first.Category);
        Assert.AreEqual(64, first.Sha256!.Length);
        Assert.AreNotEqual(0L, first.StructureHash);
        Assert.IsNull(unique.GroupKey);
        Assert.AreNotEqual(first.Sha256, unique.Sha256);
    }

    [TestMethod]
    public async Task CrossLibraryCopiesAreReportedButNeverMerged()
    {
        using var scenario = new DedupScenario();
        var library = scenario.RegisterRoot("library");
        scenario.WriteText("library/kept.png", "shared-content");
        var staging = scenario.UnregisteredRoot("holding");
        scenario.WriteText("holding/incoming.png", "shared-content");

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request(
            [
                DedupAnalysisFactory.Source(library, "library", DedupSourceRole.RegisteredLibrary),
                DedupAnalysisFactory.Source(staging, "holding"),
            ]));

        Assert.AreEqual(1, plan.Statistics.ByteDuplicateGroups);
        Assert.AreEqual(2, plan.Statistics.ByteDuplicateFiles);
        Assert.HasCount(2, plan.Groups[0].MemberIdentities);
        Assert.HasCount(2, plan.AcceptedSources);
        Assert.IsEmpty(plan.RejectedSources);

        // A cross-library copy is exactly the case a preview must not resolve by itself.
        Assert.IsFalse(plan.Groups[0].IdentityMergeProposed);
    }

    [TestMethod]
    public async Task EqualNamesWithDifferentContentAreNotEvidence()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("memes");
        scenario.WriteText("memes/one/meme.png", "first-conten");
        scenario.WriteText("memes/two/meme.png", "second-conte");
        scenario.WriteText("memes/three/meme.png", "third-conten");

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "memes")]));

        Assert.IsEmpty(plan.Groups);
        Assert.AreEqual(0, plan.Statistics.ByteDuplicateFiles);
        Assert.AreEqual(3, plan.Statistics.AnalyzedFiles);
        Assert.IsTrue(DedupAnalysisFactory.HasRelation(plan, "one/meme.png", AssetRelation.SameNameDifferentContent));
        Assert.AreEqual(DedupCategory.SameNameDifferentContent, DedupAnalysisFactory.Item(plan, "two/meme.png").Category);
        Assert.IsNull(DedupAnalysisFactory.Item(plan, "three/meme.png").GroupKey);
    }

    [TestMethod]
    public async Task EqualLengthWithDifferentBytesIsExplicitlyRejected()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("frames");
        scenario.WritePatterned("frames/left.png", 4096, seed: 1);
        scenario.WritePatterned("frames/right.png", 4096, seed: 2);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "frames")]));

        Assert.IsEmpty(plan.Groups);
        var left = DedupAnalysisFactory.Item(plan, "left.png");
        var right = DedupAnalysisFactory.Item(plan, "right.png");
        Assert.AreEqual(left.Length, right.Length);
        Assert.AreNotEqual(left.Sha256, right.Sha256);
        Assert.IsTrue(DedupAnalysisFactory.HasRelation(plan, "left.png", AssetRelation.SameLengthDifferentContent));
        Assert.AreEqual(DedupCategory.SameLengthDifferentContent, left.Category);
    }

    [TestMethod]
    public async Task ZeroByteFilesAreDuplicatesAndSpendNoReadBudget()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("empty");
        scenario.WriteFile("empty/first.bin", []);
        scenario.WriteFile("empty/second.bin", []);
        scenario.WriteFile("empty/nested/third.bin", []);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "empty")]));

        Assert.AreEqual(1, plan.Statistics.ByteDuplicateGroups);
        Assert.AreEqual(3, plan.Statistics.ByteDuplicateFiles);
        Assert.AreEqual(0L, plan.Groups[0].Length);
        Assert.AreEqual(0L, plan.Statistics.ByteDuplicateBytes);
        Assert.AreEqual(0L, plan.Statistics.ReadBytes);
    }

    [TestMethod]
    public async Task ContentBeyondTheStreamBufferIsHashedCompletely()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("bulk");
        const int Length = 512 * 1024;
        scenario.WritePatterned("bulk/alpha.bin", Length, seed: 3);
        scenario.WritePatterned("bulk/beta.bin", Length, seed: 3);
        scenario.WritePatterned("bulk/gamma.bin", Length, seed: 4);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "bulk")]));

        Assert.AreEqual(1, plan.Statistics.ByteDuplicateGroups);
        Assert.AreEqual(2, plan.Statistics.ByteDuplicateFiles);
        Assert.AreEqual(3L * Length, plan.Statistics.ReadBytes);
        Assert.AreNotEqual(
            DedupAnalysisFactory.StructureHash(plan, "alpha.bin"),
            DedupAnalysisFactory.StructureHash(plan, "gamma.bin"));
    }

    [TestMethod]
    public async Task AFileWithAUniqueLengthIsNeverRead()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("varied");
        scenario.WritePatterned("varied/one.bin", 1234);
        scenario.WritePatterned("varied/two.bin", 12);
        scenario.WritePatterned("varied/three.bin", 4321);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "varied")]));

        Assert.AreEqual(0, plan.Statistics.AnalyzedFiles);
        Assert.AreEqual(0L, plan.Statistics.ReadBytes);
        Assert.AreEqual(3, plan.Statistics.NotReadFiles);
        Assert.IsTrue(plan.Items.All(item => item.SkipReason == DedupSkipReason.NoLengthCandidate));
        Assert.AreEqual(DedupAnalysisStatus.Completed, plan.Summary.Status);
    }

    [TestMethod]
    public async Task ExcludedNamesAreReportedAsExcludedAndNeverGrouped()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("mixed");
        scenario.WriteText("mixed/kept.bin", "alpha");
        scenario.WriteText("mixed/kept-copy.bin", "alpha");
        scenario.WriteText("mixed/partial.part", "alpha");

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "mixed")]));

        Assert.AreEqual(1, plan.Statistics.ByteDuplicateGroups);
        Assert.AreEqual(3, plan.Statistics.ObservedEntries);
        var excluded = DedupAnalysisFactory.Item(plan, "partial.part");
        Assert.AreEqual(DedupSkipReason.ExcludedByDiscoveryPolicy, excluded.SkipReason);
        Assert.AreEqual(DedupPlanItemState.NotAnalyzed, excluded.State);
        Assert.IsNull(excluded.GroupKey);
        Assert.IsNull(excluded.Sha256);
    }
}

/// <summary>
/// Proves that animations, re-encodes, revisions, sidecars and junctions stay separate assets and
/// are never offered as duplicates, however similar they look.
/// </summary>
[TestClass]
public sealed class DedupVariantTests
{
    [TestMethod]
    public async Task AnimationAndEncodingVariantsRemainDifferentAssets()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("art");
        scenario.WritePatterned("art/pair/hero.png", 2048, seed: 5);
        scenario.WritePatterned("art/pair/hero.gif", 2048, seed: 6);
        scenario.WritePatterned("art/pair/hero.jpg", 2048, seed: 7);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "art")]));

        // Same length and same base name, yet nothing is grouped: none of these is a byte duplicate.
        Assert.IsEmpty(plan.Groups);
        Assert.AreEqual(3, plan.Statistics.AnalyzedFiles);
        Assert.IsTrue(DedupAnalysisFactory.HasRelation(plan, "pair/hero.png", AssetRelation.AnimatedVariant));
        Assert.IsTrue(DedupAnalysisFactory.HasRelation(plan, "pair/hero.png", AssetRelation.EncodingVariant));
        Assert.IsTrue(DedupAnalysisFactory.HasRelation(plan, "pair/hero.jpg", AssetRelation.EncodingVariant));
        Assert.AreEqual(DedupCategory.CompanionOrVariant, DedupAnalysisFactory.Item(plan, "pair/hero.png").Category);
        Assert.IsNull(DedupAnalysisFactory.Item(plan, "pair/hero.gif").GroupKey);
    }

    [TestMethod]
    public async Task SidecarFilesAreRelationsNotDuplicates()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("clips");
        scenario.WritePatterned("clips/clip/clip.mp4", 8192, seed: 8);
        scenario.WritePatterned("clips/clip/clip.json", 8192, seed: 12);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "clips")]));

        Assert.IsEmpty(plan.Groups);
        Assert.AreEqual(2, plan.Statistics.AnalyzedFiles);
        Assert.IsTrue(DedupAnalysisFactory.HasRelation(plan, "clip/clip.mp4", AssetRelation.CompanionFile));
        Assert.IsTrue(DedupAnalysisFactory.HasRelation(plan, "clip/clip.json", AssetRelation.CompanionFile));
        Assert.AreEqual(DedupCategory.CompanionOrVariant, DedupAnalysisFactory.Item(plan, "clip/clip.mp4").Category);
        Assert.IsNull(DedupAnalysisFactory.Item(plan, "clip/clip.json").GroupKey);
    }

    [TestMethod]
    public async Task IdenticalRevisionsStayTwoFilesDespiteEqualBytes()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("revisions");
        scenario.WritePatterned("revisions/art/hero.png", 3072, seed: 9);
        scenario.WritePatterned("revisions/art/v2/hero.png", 3072, seed: 9);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "revisions")]));

        Assert.AreEqual(1, plan.Statistics.ByteDuplicateGroups);
        Assert.AreEqual(2, plan.Statistics.ByteDuplicateFiles);
        Assert.IsFalse(plan.Groups[0].IdentityMergeProposed);
        Assert.AreEqual("art/hero.png,art/v2/hero.png", DedupAnalysisFactory.PathList(plan));
    }

    [TestMethod]
    public async Task AJunctionIsReportedAsItsOwnEntityAndNeverFollowed()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("linked-root");
        scenario.WritePatterned("linked-root/real/kept.bin", 4096, seed: 11);
        scenario.WritePatterned("linked-root/elsewhere/loop-copy.bin", 4096, seed: 11);

        // The junction points at a directory holding a byte-identical copy. If the walk followed it,
        // the same content would be reported twice and the copy counted a second time.
        var elsewhere = scenario.AbsolutePath(Path.Combine("linked-root", "elsewhere"));
        var link = scenario.TrackJunction(Path.Combine(scenario.Root, "linked-root", "mirror"));
        DedupScenario.CreateDirectoryJunction(link, elsewhere);

        var plan = await scenario.AnalyzeAsync(
            DedupAnalysisFactory.Request([DedupAnalysisFactory.Source(root, "linked-root")]));

        Assert.AreEqual(1, plan.Statistics.ByteDuplicateGroups);
        Assert.AreEqual(2, plan.Statistics.ByteDuplicateFiles);
        Assert.AreEqual(
            "elsewhere/loop-copy.bin,mirror,real/kept.bin",
            DedupAnalysisFactory.PathList(plan));

        var junction = DedupAnalysisFactory.Item(plan, "mirror");
        Assert.AreEqual(DedupSkipReason.ReparsePoint, junction.SkipReason);
        Assert.IsNull(junction.Sha256);
        Assert.IsFalse(plan.Items.Any(item => item.PathText.StartsWith("mirror/", StringComparison.Ordinal)));
        Assert.AreEqual(DedupAnalysisStatus.Completed, plan.Summary.Status);
    }
}
