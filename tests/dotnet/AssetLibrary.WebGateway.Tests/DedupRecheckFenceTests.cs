using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// Proves where a recheck's filesystem work happens relative to the fence that makes its verdict durable,
/// and that the verdict is compared against the library's current version in the same step that files it.
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
        // The version is dropped while the scan runs, which is what a lost lease does: the key no longer
        // resolves, and the scan discovers that only after the walk has finished.
        scenario.DropVersionDuringScan = true;
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

    /// <summary>
    /// The case retention makes normal: the version being rechecked is still readable, and a newer analysis
    /// of the same library has already been filed. A run that only asks "is my version still there" answers
    /// yes and then overwrites the newer report with its own older one, which is what the previous round's
    /// probe measured. The verdict must instead be refused, and the newer report must still be the one a
    /// reader sees.
    /// </summary>
    [TestMethod]
    public async Task ALateVerdictNeverReplacesANewerAnalysisOfTheSameLibrary()
    {
        using var scenario = await DedupRecheckFenceFixture.CreateAsync();
        scenario.NewAnalysisDuringScan = true;
        await scenario.RunRecheckAsync();

        var verified = scenario.VerifiedVersion;
        var current = scenario.CurrentVersion;
        Assert.IsNotNull(current, "The library must still have a current report after the late verdict.");
        Assert.AreNotEqual(
            verified.TaskId,
            current.Value.TaskId,
            "A late verdict for the old version must never become the library's current report.");
        Assert.AreEqual(
            scenario.VerifiedDigest,
            scenario.DigestOf(verified),
            "The version that was rechecked stays readable: retention keeps it, which is why the check cannot be \"does my key resolve\".");

        var filed = scenario.Run;
        Assert.IsNotNull(filed, "The recheck must still file an outcome for the version it was asked about.");
        Assert.IsFalse(filed.Completed, "A superseded version must not be answered with stale evidence.");
        Assert.IsFalse(filed.PlanStillCurrent, "It must never read as one whose files are unchanged.");
        Assert.AreEqual("dedup_version_conflict", filed.FailureCode);

        // The fence property is unchanged by the comparison: bytes were read outside it, the verdict inside.
        Assert.IsGreaterThan(0, scenario.ScanReads, "The scan really ran before the newer analysis landed.");
        Assert.AreEqual(0, scenario.ReadsWhileFenced, "No file may be read while the durable commit is open.");
        Assert.IsGreaterThan(0, scenario.Commits, "The compare-and-file must still happen inside a fenced commit.");
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
