using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.Modules.AssetIdentity.Dedup.Application;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// The job, page, recheck and row vocabulary of the dedup workbench, asserted from the side that
/// produces it. The page's decoder (<c>apps/web/src/dedup/dedupResponses.ts</c>) refuses any answer
/// whose keys or JSON token kinds do not match what it reads, while the browser suite runs against
/// hand-written fixtures — so a renamed field would pass every other gate and only fail in front of a
/// user. These cases serialize the real Host builders and check the exact vocabulary that decoder
/// requires, including the null-versus-absent distinction it treats as meaningful.
/// </summary>
[TestClass]
public sealed class DedupReportWireTests
{
    private static readonly string[] ChangedPath = ["photos/beach.png"];

    [TestMethod]
    public void JobAnswerCarriesTheDecoderKeysAndNoNullableIsEncodedAsAbsent()
    {
        var view = new DedupJobView(
            DedupWireSample.TaskId,
            new LibraryId(DedupWireSample.LibraryId),
            "设计素材",
            "succeeded",
            CancellationRequested: false,
            CanCancel: false,
            CanRetry: true,
            ReportAvailable: true,
            AnalysisVersion: DedupWireSample.Version(1),
            CreatedAt: new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 9, 17, 9, 4, 0, TimeSpan.Zero),
            FailureCode: null,
            RetentionBoundary: DedupJobContractText.RetentionBoundary,
            Limits: DedupAnalysisLimits.Default);

        var wire = Decode(TrialDedupJson.Job(view));

        Wire.Exact(
            wire,
            "task_id",
            "library_id",
            "library_display_name",
            "state",
            "cancellation_requested",
            "can_cancel",
            "can_retry",
            "report_available",
            "analysis_version",
            "created_at",
            "updated_at",
            "failure_code",
            "retention_notice",
            "read_only_notice",
            "limits");
        Wire.Text(wire, "task_id", DedupWireSample.TaskId.ToString("D"));
        Wire.Text(wire, "retention_notice", DedupJobContractText.RetentionBoundary);
        Wire.Text(wire, "read_only_notice", DedupJobContractText.ReadOnlyBoundary);
        // The decoder accepts a missing failure_code, but an absent key and an explicit null differ
        // only by accident here, so the answer states it.
        Wire.Text(wire, "failure_code", null);
        Assert.IsTrue(wire["report_available"]!.GetValue<bool>());

        // The budget a page states is the budget the server accepted, so it travels with the job.
        var limits = Wire.Node(wire, "limits");
        Wire.Exact(limits, "maximum_files", "maximum_bytes", "maximum_file_bytes", "hash_concurrency");
        Assert.AreEqual(
            DedupAnalysisLimits.Default.MaximumBytes,
            limits["maximum_bytes"]!.GetValue<long>());
        Assert.AreEqual(
            DedupAnalysisLimits.Default.MaximumFileBytes,
            limits["maximum_file_bytes"]!.GetValue<int>());
    }

    [TestMethod]
    public void PageAnswerStatesWhetherAReportExistsSeparatelyFromItsRows()
    {
        var report = DedupWireSample.Report.Retained();
        var page = new DedupResultsPage(
            DedupWireSample.TaskId,
            DedupWireSample.Version(1),
            DedupFindingKind.ByteDuplicateGroup,
            [DedupWireSample.Report.Group()],
            [],
            Offset: 0,
            PageSize: DedupJobContractText.DefaultPageSize,
            Total: 1,
            NextCursor: "cursor-1",
            ReportAvailable: true,
            Summary: DedupJobViewFactory.Summary(new DedupReportKey(DedupWireSample.TaskId, 1), report));

        var wire = Decode(TrialDedupPageJson.Page(page));

        Wire.Exact(
            wire,
            "task_id",
            "analysis_version",
            "kind",
            "kind_text",
            "offset",
            "page_size",
            "total",
            "next_cursor",
            "report_available",
            "groups",
            "items",
            "summary");
        Wire.Text(wire, "kind", nameof(DedupFindingKind.ByteDuplicateGroup));
        Wire.Text(wire, "kind_text", DedupText.Describe(DedupFindingKind.ByteDuplicateGroup));
        Wire.Text(wire, "next_cursor", "cursor-1");
        Assert.AreEqual(DedupJobContractText.DefaultPageSize, wire["page_size"]!.GetValue<int>());
        Assert.IsTrue(wire["report_available"]!.GetValue<bool>());
        var summary = Wire.Node(wire, "summary");
        Wire.Exact(
            summary,
            "policy_version",
            "analyzed_at",
            "plan_digest",
            "statistics",
            "plan",
            "duplicate_group_count",
            "duplicate_file_count",
            "unverified_count",
            "unreadable_count",
            "unique_count",
            "retained_item_count",
            "truncated",
            "retention_notice",
            "analysis_version");
        // The version of a report is its own identity, and the page binds every later call to it.
        Wire.Text(summary, "analysis_version", DedupWireSample.Version(1));
        Wire.Exact(
            Wire.Node(summary, "statistics"),
            "observed_entries",
            "analyzed_files",
            "not_read_files",
            "failed_files",
            "skipped_files",
            "byte_duplicate_groups",
            "byte_duplicate_files",
            "byte_duplicate_bytes",
            "read_bytes",
            "additional_read_attempts");
        var plan = Wire.Node(summary, "plan");
        Wire.Exact(
            plan,
            "status",
            "status_text",
            "unreadable_paths",
            "incomplete_reason_count",
            "scan_bounds_reached",
            "failure_code",
            "source_failures");
        Wire.Exact(Wire.Items(plan, "source_failures")[0]!, "source_id", "reason_code");
    }

    [TestMethod]
    public void GroupAndItemRowsCarryEveryFieldTheListAndTheDetailRead()
    {
        var group = Decode(TrialDedupJson.Group(DedupWireSample.Report.Group()));

        Wire.Exact(group, "group_key", "length", "evidence_hash", "member_count", "identity_merge_proposed", "members");
        var member = Wire.Items(group, "members")[0]!;
        Wire.Exact(
            member,
            "source_id",
            "root",
            "relative_path",
            "name",
            "length",
            "sha256",
            "structure_hash",
            "last_write_time_utc",
            "state",
            "state_text",
            "read_state",
            "read_state_text",
            "failure",
            "skip_reason",
            "group_key",
            "category",
            "relations",
            "relation_notes");
        Wire.Text(member, "name", "beach.png");
        Wire.Text(member, "state", nameof(DedupPlanItemState.Analyzed));
        Wire.Text(member, "read_state", nameof(DedupItemReadState.ContentVerified));
        // The page shows the server's sentence for a state instead of translating the enum itself.
        Wire.Text(member, "state_text", DedupText.Describe(DedupPlanItemState.Analyzed));
        Wire.Text(member, "read_state_text", DedupText.Describe(DedupItemReadState.ContentVerified));
        Assert.AreEqual(2, Wire.Items(group, "members").Count);
    }


    private static JsonObject Decode(JsonObject payload) => Wire.RoundTrip(payload);
}
