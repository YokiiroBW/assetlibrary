
namespace AssetLibrary.ReadCore.Tests;

/// <summary>
/// Proves the byte ceiling is settled on the bytes a read really returned rather than on the length
/// discovery recorded. A file whose real size no longer matches what the scan saw must not be able to
/// spend budget nobody granted it, a failed read must not be charged for bytes it never read, the
/// reported read volume must be the real one, and a reader that answers outside its allowance must
/// leave a bounded, unverified run instead of a quietly wrong total.
/// </summary>
[TestClass]
public sealed class DedupGrowthBudgetTests
{
    /// <summary>The size discovery reports, which is what a read pass permits a file.</summary>
    private const int DiscoveredLength = 160;

    [TestMethod]
    public async Task AFileBiggerThanTheUnspentBudgetIsNeverReadInFull()
    {
        const int RealLength = 300;
        const int FillerCount = 3;
        const int Budget = (FillerCount * RealLength) + 100;
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("outgrown");
        for (var index = 0; index < FillerCount; index++)
        {
            scenario.WriteText($"outgrown/filler{index}.bin", new string((char)('a' + index), RealLength));
        }

        scenario.WriteText("outgrown/outgrew.bin", new string('z', RealLength));
        var reader = new RecordingContentReader();

        var plan = await DedupAnalysisFactory
            .Create(scenario, reader)
            .AnalyzeAsync(
                DedupAnalysisFactory.Request(
                    [DedupAnalysisFactory.Source(root, "outgrown")],
                    new DedupAnalysisLimits(
                        MaximumFiles: 100,
                        MaximumBytes: Budget,
                        MaximumFileBytes: 1_000)),
                CancellationToken.None);

        // The three filler reads really spent 900 of the 1000 bytes, so only 100 were unspent when the last
        // file was reached and the reader refused the 300-byte file instead of hashing it: an allowance of
        // 100 can never become the 300 a whole file needs. The report states the 900 bytes it really read,
        // not the 1200 the discovery lengths would have added up to.
        Assert.HasCount(FillerCount + 1, reader.Requests);
        Assert.IsTrue(reader.Requests.All(request => request.MaximumBytes <= RealLength));
        Assert.AreEqual(FillerCount * (long)RealLength, plan.Statistics.ReadBytes);
        Assert.AreEqual(FillerCount, plan.Statistics.AnalyzedFiles);
        Assert.AreEqual(1, plan.Statistics.FailedFiles);
        Assert.IsTrue(plan.Summary.ScanBoundsReached);
    }

    [TestMethod]
    public async Task ReadBytesIsWhatWasReallyReadAndNotTheLengthDiscoverySaw()
    {
        const int RealLength = 200;
        const int ReportedLength = 1_000;
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("stale-lengths");
        scenario.WriteText("stale-lengths/alpha.bin", new string('a', RealLength));
        scenario.WriteText("stale-lengths/beta.bin", new string('b', RealLength));
        scenario.WriteText("stale-lengths/gamma.bin", new string('c', RealLength));
        // Discovery reported 1000 bytes for every file while each really holds 200. A report that charged the
        // lengths discovery saw would total 3000 while the bytes on disk total 600, so the number that
        // appears says which of the two the run is accounted against.
        var reader = new RecordingContentReader();

        var plan = await DedupAnalysisFactory
            .Create(scenario, reader, new UnderstatingDiscovery(ReportedLength))
            .AnalyzeAsync(
                DedupAnalysisFactory.Request(
                    [DedupAnalysisFactory.Source(root, "stale-lengths")],
                    new DedupAnalysisLimits(MaximumFiles: 100, MaximumBytes: 4_000, MaximumFileBytes: 1_000)),
                CancellationToken.None);

        Assert.HasCount(3, plan.Items);
        Assert.IsTrue(reader.Results.All(result => result.Length == RealLength));
        Assert.AreEqual(3L * RealLength, plan.Statistics.ReadBytes);
        Assert.AreEqual(3, plan.Statistics.AnalyzedFiles);
        Assert.AreEqual(
            plan.Items.Sum(item => item.Length),
            plan.Statistics.ReadBytes,
            "The reported read volume must be the sum of the lengths the reads really verified.");
    }

    [TestMethod]
    public async Task AFileThatGrewPastItsPermitIsRefusedInsteadOfReadInFull()
    {
        const int RealLength = 200;
        const int ReportedLength = 40;
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("shrunk");
        scenario.WriteText("shrunk/alpha.bin", new string('a', RealLength));
        scenario.WriteText("shrunk/beta.bin", new string('b', RealLength));
        // Discovery reported 40 bytes for every file while each really holds 200. A whole file is only read
        // under the permit its own observed length earned, so both files are refused instead of being hashed
        // on 160 bytes each of allowance that was never granted, and neither is charged a read volume it
        // never contributed to.
        var reader = new RecordingContentReader();

        var plan = await DedupAnalysisFactory
            .Create(scenario, reader, new UnderstatingDiscovery(ReportedLength))
            .AnalyzeAsync(
                DedupAnalysisFactory.Request(
                    [DedupAnalysisFactory.Source(root, "shrunk")],
                    new DedupAnalysisLimits(MaximumFiles: 100, MaximumBytes: 4_000, MaximumFileBytes: 1_000)),
                CancellationToken.None);

        Assert.HasCount(2, reader.Requests);
        Assert.IsTrue(reader.Requests.All(request => request.MaximumBytes == ReportedLength));
        Assert.AreEqual(2, plan.Statistics.FailedFiles);
        Assert.AreEqual(0, plan.Statistics.AnalyzedFiles);
        Assert.AreEqual(0L, plan.Statistics.ReadBytes);
        Assert.IsTrue(plan.Items.All(item => item.Sha256 is null));
        Assert.IsTrue(plan.Summary.ScanBoundsReached);
    }
    [TestMethod]
    public async Task AReaderThatReturnsMoreThanItWasPermittedLeavesAnUnverifiedBoundedRun()
    {
        using var scenario = new DedupScenario();
        var root = scenario.RegisterRoot("disregarding");
        scenario.WriteText("disregarding/alpha.bin", new string('a', DiscoveredLength));
        scenario.WriteText("disregarding/beta.bin", new string('b', DiscoveredLength));
        var reader = new DisregardingContentReader(claimedLength: 4_096);

        var plan = await DedupAnalysisFactory
            .Create(scenario, reader, new UnderstatingDiscovery(DiscoveredLength))
            .AnalyzeAsync(
                DedupAnalysisFactory.Request(
                    [DedupAnalysisFactory.Source(root, "disregarding")],
                    new DedupAnalysisLimits(MaximumFiles: 100, MaximumBytes: 400, MaximumFileBytes: 1_000)),
                CancellationToken.None);

        // A reader that answers outside its allowance has spent bytes nobody authorised: every answer it
        // gave in that batch is recorded as a changed read, no later file is read, and the run states it was
        // bounded. Neither answer is counted as read volume, because neither verified a byte of the file.
        Assert.HasCount(2, reader.Requests);
        Assert.AreEqual(2, plan.Statistics.FailedFiles);
        Assert.AreEqual(0, plan.Statistics.AnalyzedFiles);
        Assert.AreEqual(0L, plan.Statistics.ReadBytes);
        Assert.IsTrue(plan.Items.All(item => item.Sha256 is null));
        Assert.IsTrue(plan.Summary.ScanBoundsReached);
    }
}
