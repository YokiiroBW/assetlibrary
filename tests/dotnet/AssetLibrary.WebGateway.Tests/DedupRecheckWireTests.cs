using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// The recheck vocabulary of the dedup workbench, asserted from the side that produces it. A recheck is
/// filed as a durable background task, so its answer has two forms — a receipt and an outcome — and the
/// page's decoder (<c>apps/web/src/dedup/dedupResponses.ts</c>) decides what to render from the state
/// field alone. A renamed field or a dropped list would pass every other gate and only fail in front of a
/// user, so these cases serialize the real Host builder and check the exact vocabulary that decoder reads.
/// </summary>
[TestClass]
public sealed class DedupRecheckWireTests
{
    private static readonly string[] ChangedPath = ["photos/beach.png"];

    [TestMethod]
    public void RecheckAnswerKeepsANotRecheckedPlanDistinguishableFromACurrentOne()
    {
        var state = new DedupRecheckState(
            RecheckRunState.Completed,
            new DedupRecheckRun(
                Completed: true,
                Status: DedupRecountStatus.SourceChanged,
                PlanStillCurrent: false,
                Reasons: ["内容已变化：photos/beach.png"],
                ChangedPaths: ChangedPath,
                DisappearedPaths: [],
                NewPaths: [],
                PlanDigest: "digest-4f2a9c7b",
                PreviousPlanDigest: "digest-0f0f0f0f",
                VerifiedGeneration: 1,
                AnalysisVersion: DedupWireSample.Version(2),
                FailureCode: null),
            DedupWireSample.TaskId,
            DedupWireSample.RecheckTaskId,
            "设计素材");

        var wire = Decode(TrialDedupPageJson.Recheck(state));

        Wire.Exact(
            wire,
            "state",
            "state_text",
            "task_id",
            "recheck_task_id",
            "completed",
            "status",
            "status_text",
            "plan_still_current",
            "reasons",
            "changed_paths",
            "disappeared_paths",
            "new_paths",
            "plan_digest",
            "previous_plan_digest",
            "analysis_version",
            "failure_code",
            "report_available",
            "retention_notice",
            "read_only_notice");
        Wire.Text(wire, "state", "completed");
        Wire.Text(wire, "status", "source_changed");
        Wire.Text(wire, "recheck_task_id", DedupWireSample.RecheckTaskId.ToString("D"));
        Wire.Text(wire, "status_text", DedupText.Describe(DedupRecountStatus.SourceChanged));
        Wire.Text(wire, "previous_plan_digest", "digest-0f0f0f0f");
        Assert.IsFalse(wire["plan_still_current"]!.GetValue<bool>());
        CollectionAssert.AreEqual(ChangedPath, Wire.Items(wire, "changed_paths").Select(Wire.Text).ToArray());
    }

    /// <summary>
    /// A recheck runs in the background, so its first answer is a receipt. "Queued" must travel as its
    /// own state with every list present and empty: a page that read a shorter answer as "nothing
    /// changed" would present an unfinished check as a verified plan.
    /// </summary>
    [TestMethod]
    public void AnAcceptedRecheckStatesThatItIsStillPendingInsteadOfAnsweringAboutNothing()
    {
        var state = new DedupRecheckState(
            RecheckRunState.Pending,
            null,
            DedupWireSample.TaskId,
            DedupWireSample.RecheckTaskId,
            "设计素材");

        var wire = Decode(TrialDedupPageJson.Recheck(state));

        Wire.Text(wire, "state", "pending");
        Assert.IsFalse(wire["completed"]!.GetValue<bool>());
        Assert.IsFalse(wire["report_available"]!.GetValue<bool>());
        Assert.IsFalse(wire["plan_still_current"]!.GetValue<bool>());
        Wire.Text(wire, "status", string.Empty);
        foreach (var key in new[] { "reasons", "changed_paths", "disappeared_paths", "new_paths" })
        {
            Assert.IsEmpty(Wire.Items(wire, key), key);
        }
    }

    /// <summary>
    /// A refused recheck is not a clean plan. The retained version it named is gone or was replaced, so
    /// the answer states that no comparison happened instead of leaving the page to infer it from an
    /// empty change list.
    /// </summary>
    [TestMethod]
    public void ARefusedRecheckStatesThatNoComparisonHappened()
    {
        var state = new DedupRecheckState(
            RecheckRunState.Refused,
            new DedupRecheckRun(
                Completed: false,
                Status: DedupRecountStatus.SourceChanged,
                PlanStillCurrent: false,
                Reasons: ["要复核的报告版本已不再保留。"],
                ChangedPaths: [],
                DisappearedPaths: [],
                NewPaths: [],
                PlanDigest: "digest-4f2a9c7b",
                PreviousPlanDigest: null,
                VerifiedGeneration: 3,
                AnalysisVersion: string.Empty,
                FailureCode: "dedup_report_not_retained"),
            DedupWireSample.TaskId,
            DedupWireSample.RecheckTaskId,
            "设计素材");

        var wire = Decode(TrialDedupPageJson.Recheck(state));

        Wire.Text(wire, "state", "refused");
        Wire.Text(wire, "failure_code", "dedup_report_not_retained");
        Assert.IsFalse(wire["completed"]!.GetValue<bool>());
        Assert.IsFalse(wire["report_available"]!.GetValue<bool>());
        Assert.IsNotEmpty(Wire.Items(wire, "reasons"));
    }

    private static JsonObject Decode(JsonObject payload) => Wire.RoundTrip(payload);
}
