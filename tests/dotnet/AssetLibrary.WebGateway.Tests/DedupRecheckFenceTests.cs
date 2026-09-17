using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// Proves where a recheck's filesystem work happens relative to the fence that makes its verdict durable.
/// A recheck walks and hashes real files, so the pass must run with no fence held: an implementation that
/// keeps the durable task's row open for the length of the walk turns one recheck into a lock every other
/// attempt on the same task waits on, and a slow share into a stalled workbench. Only the verdict belongs
/// inside the fence, and these tests measure exactly that on a synthetic library.
/// </summary>
[TestClass]
public sealed class DedupRecheckFenceTests
{
    [TestMethod]
    public async Task ARecheckScansWithNoFenceHeldAndFilesOnlyItsVerdictUnderOne()
    {
        using var scenario = await DedupRecheckFenceFixture.CreateAsync();
        await scenario.RunRecheckAsync();

        // The walk is the expensive part, and it really ran: reading nothing would prove nothing.
        Assert.IsGreaterThan(0, scenario.ScanReads, "The recheck must really read the retained sources.");
        Assert.AreEqual(0, scenario.ReadsWhileFenced, "No file may be read while the durable commit is open.");

        // The verdict is still filed under a fence, so a lease that expired mid-scan cannot leave it behind.
        Assert.IsGreaterThan(0, scenario.Commits, "The verdict must be filed inside a fenced commit.");
        Assert.IsLessThanOrEqualTo(
            1,
            scenario.MaximumOpenCommits,
            "The fence must never be opened twice at once on one attempt.");
        Assert.AreEqual(0, scenario.OpenCommits, "The fence must be released when the attempt ends.");
    }

    [TestMethod]
    public async Task AVerdictIsRefusedInsteadOfFiledWhenTheVersionItVerifiedIsNoLongerRetained()
    {
        using var scenario = await DedupRecheckFenceFixture.CreateAsync();
        // The version is dropped while the scan runs, which is what a lost lease or a newer analysis does:
        // the key no longer resolves, and the scan discovers that only after the walk has finished.
        scenario.ReplacedDuringScan = true;
        await scenario.RunRecheckAsync();

        var filed = scenario.Run;
        Assert.IsNotNull(filed, "The recheck must still file an outcome for the version it was asked about.");
        Assert.IsFalse(filed.Completed, "A version that is gone must not be answered with stale evidence.");
        Assert.IsFalse(
            filed.PlanStillCurrent,
            "A report that is gone must never read as one whose files are unchanged.");
        Assert.AreEqual("dedup_version_conflict", filed.FailureCode);
        Assert.AreEqual(1L, filed.VerifiedGeneration, "The outcome must still name the version it was asked about.");

        // And the run is still correctly fenced: a verdict filed under a fence, bytes read outside one.
        Assert.IsGreaterThan(0, scenario.ScanReads, "The scan really ran before the version disappeared.");
        Assert.AreEqual(0, scenario.ReadsWhileFenced, "No file may be read while the durable commit is open.");
        Assert.IsGreaterThan(0, scenario.Commits, "The verdict must be filed inside a fenced commit.");
    }

    [TestMethod]
    public async Task ARecheckThatIsCancelledMidScanReadsNoFurtherAndFilesNothing()
    {
        using var scenario = await DedupRecheckFenceFixture.CreateAsync();
        scenario.CancelDuringScan = true;
        await scenario.RunRecheckAsync();

        Assert.AreEqual(
            0,
            scenario.Commits,
            "A cancelled scan must not open the fence, so a later attempt owns the version.");
        Assert.IsTrue(scenario.ScanWasCancelled, "The scan must observe the cancellation instead of running on.");
        Assert.IsNull(scenario.Run, "Nothing may be filed for an attempt that never reached a verdict.");
    }
}
