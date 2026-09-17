using AssetLibrary.CoreServer.Hosting.Trial;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// The refusal vocabulary. Every answer the page shows for a refused call comes from here, so the code
/// a Docker or service client can branch on and the sentence the page prints are asserted together
/// instead of being maintained in two places.
/// </summary>
[TestClass]
public sealed class DedupRefusalWireTests
{
    [TestMethod]
    public void RefusalAnswersCarryAReadableCodeAndTheStatusThePageShows()
    {
        var wire = Wire.RoundTrip(TrialDedupJson.Error(409, "idempotency_conflict"));

        Wire.Exact(wire, "error", "status");
        Assert.AreEqual(409, wire["status"]!.GetValue<int>());
        Wire.Exact(Wire.Node(wire, "error"), "code", "message");
        Wire.Text(Wire.Node(wire, "error"), "code", "idempotency_conflict");
        Wire.Text(Wire.Node(wire, "error"), "message", "请求与已有任务或当前计划不一致。");
    }

    /// <summary>
    /// A stale plan or a busy library is a refusal a caller can act on, an unreachable store is a
    /// retryable 503, and a discarded report is a 404 rather than an empty 200.
    /// </summary>
    [TestMethod]
    public void EveryRefusalCodeMapsToAStatusTheCallerCanActOn()
    {
        Assert.AreEqual(400, TrialDedupJson.Status("invalid_cursor"));
        Assert.AreEqual(400, TrialDedupJson.Status("dedup_group_not_found"));
        Assert.AreEqual(403, TrialDedupJson.Status("dedup_library_not_allowed"));
        Assert.AreEqual(403, TrialDedupJson.Status("permission_denied"));
        Assert.AreEqual(404, TrialDedupJson.Status("dedup_report_not_retained"));
        Assert.AreEqual(404, TrialDedupJson.Status("dedup_task_missing"));
        Assert.AreEqual(409, TrialDedupJson.Status("dedup_already_running"));
        Assert.AreEqual(409, TrialDedupJson.Status("dedup_version_conflict"));
        Assert.AreEqual(409, TrialDedupJson.Status("results_too_large"));
        Assert.AreEqual(503, TrialDedupJson.Status("service_unavailable"));
        Assert.AreEqual(503, TrialDedupJson.Status("storage_unavailable"));
    }
}
