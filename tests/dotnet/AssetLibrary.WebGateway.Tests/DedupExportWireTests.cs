using System.Text.Json;
using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// The exported plan document, asserted from the side that produces it. The page reads it with the
/// same exact decoder it uses for every other answer, so a renamed field or a number a browser cannot
/// hold exactly would pass every other gate and only fail after delivery.
/// </summary>
[TestClass]
public sealed class DedupExportWireTests
{
    [TestMethod]
    public void ExportedPlanStatesTheReadOnlyBoundaryAndKeepsItsGroupsReviewable()
    {
        var wire = Wire.RoundTrip(TrialDedupExportJson.Export(DedupWireSample.Plan.Exported()));

        Wire.Text(wire, "document_type", "assetlibrary.dedup.plan");
        Assert.AreEqual(1, wire["format_version"]!.GetValue<int>());
        // The document itself must deny that it authorises a file operation.
        Assert.IsFalse(wire["grants_file_operation"]!.GetValue<bool>());
        Wire.Text(wire, "read_only_notice", DedupJobContractText.ReadOnlyBoundary);
        Wire.Text(wire, "retention_notice", DedupJobContractText.RetentionBoundary);
        Wire.Exact(
            Wire.Node(wire, "limits"),
            "maximum_files",
            "maximum_bytes",
            "maximum_file_bytes",
            "hash_concurrency");
        Wire.Exact(
            Wire.Node(wire, "recheck"),
            "performed",
            "status",
            "reasons",
            "changed_count",
            "disappeared_count",
            "new_count",
            "performed_at",
            "plan_digest");
        Wire.Exact(
            Wire.Node(wire, "plan"),
            "status",
            "status_text",
            "unreadable_paths",
            "incomplete_reason_count",
            "scan_bounds_reached",
            "failure_code",
            "source_failures");
        // A source that stopped the run is published as an id plus a reason code, not as prose.
        Wire.Exact(Wire.Items(Wire.Node(wire, "plan"), "source_failures")[0]!, "source_id", "reason_code");
        var group = Wire.Items(wire, "groups")[0]!;
        Wire.Exact(group, "group_key", "length", "evidence_hash", "evidence_basis", "members");
        Wire.Exact(
            Wire.Items(group, "members")[0]!,
            "relative_path",
            "root",
            "length",
            "sha256",
            "structure_hash",
            "last_write_time_utc",
            "verification_state",
            "relation_note");
        // "Not rechecked" is stated rather than implied by an absent field, so a plan never looks
        // freshly verified by omission.
        Assert.IsFalse(Wire.Node(wire, "recheck")["performed"]!.GetValue<bool>());
        Wire.Text(Wire.Node(wire, "recheck"), "performed_at", null);
    }

    /// <summary>
    /// A JavaScript client can only hold integers up to 2^53-1, and the page refuses anything else
    /// rather than rounding it. The check runs on the serialized text exactly as the browser parses it.
    /// </summary>
    [TestMethod]
    public void SerializedNumbersStayInsideTheRangeThePageCanReadExactly()
    {
        var text = TrialDedupJson.Serialize(TrialDedupExportJson.Export(DedupWireSample.Plan.Exported()));
        using var document = JsonDocument.Parse(text);

        DedupWireSample.AssertSafeNumbers(document.RootElement);
        // A real library's byte ceiling is well past a 32-bit integer, so the page must receive a JSON
        // number rather than a string and still read it exactly.
        var maximumBytes = document.RootElement.GetProperty("limits").GetProperty("maximum_bytes").GetInt64();
        Assert.AreEqual(512L * 1024 * 1024 * 1024, maximumBytes);
    }
}
