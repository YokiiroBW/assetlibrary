using System.Text.Json;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// The report facts every wire case shares, built once through the module's own records. The export
/// cases and the page cases describe the same run, so they must not each grow their own idea of what
/// a statistic or a plan statement contains.
/// </summary>
internal static class DedupWireSample
{
    public static readonly Guid TaskId = Guid.Parse("88888888-8888-4888-8888-888888888888");
    public static readonly Guid SourceId = Guid.Parse("99999999-9999-4999-8999-999999999999");
    public static readonly Guid LibraryId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    /// <summary>The identity string every page call is bound to, for one attempt of the sample job.</summary>
    public static string Version(long generation) => new DedupReportKey(TaskId, generation).VersionText;

    /// <summary>
    /// A JavaScript client can only hold integers up to 2^53-1, and the page refuses anything else
    /// rather than rounding it, so the serialized text is checked exactly as the browser parses it.
    /// </summary>
    public static void AssertSafeNumbers(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                bool integer = element.TryGetInt64(out var value);
                Assert.IsTrue(integer, "every number must be an integer");
                Assert.IsLessThanOrEqualTo(
                    9_007_199_254_740_991L,
                    Math.Abs(value),
                    $"'{element}' does not fit a JavaScript safe integer");
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertSafeNumbers(item);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AssertSafeNumbers(property.Value);
                }

                break;
            default:
                break;
        }
    }

    /// <summary>
    /// The retained report and the rows a page reads out of it. Both the page cases and the export
    /// cases start here, so a statistic or a row shape is described once.
    /// </summary>
    internal static class Report
    {
        public static DedupReport Retained() => new()
        {
            AnalysisId = DedupAnalysisId.New(),
            TaskId = TaskId,
            LibraryId = new LibraryId(LibraryId),
            LibraryDisplayName = "设计素材",
            AnalyzedAt = new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero),
            PolicyVersion = DedupContractText.PolicyVersion,
            AcceptedSources = [AcceptedSource()],
            RejectedSources = [],
            AcceptedLibraryIds = [LibraryId.ToString("D")],
            Groups = [Group()],
            Unverified = [],
            Unreadable = [],
            Statistics = Statistics(),
            Summary = Summary(),
            PlanDigest = "digest-4f2a9c7b",
            Limits = DedupAnalysisLimits.Default,
            Truncated = false,
            RetentionBoundary = DedupJobContractText.RetentionBoundary,
        };

        public static DedupReportGroup Group() => new(
            "group-1",
            4096,
            "sha256-evidence",
            IdentityMergeProposed: false,
            [Item("photos/beach.png"), Item("backup/beach.png")]);

        internal static DedupReportItem Item(string relativePath) => new(
            new DedupSourceId(SourceId),
            "设计素材",
            relativePath,
            4096,
            "sha256",
            "structure",
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            DedupPlanItemState.Analyzed,
            DedupItemReadState.ContentVerified,
            DedupReadFailure.None,
            DedupSkipReason.None,
            "group-1",
            DedupCategory.ByteDuplicateGroup,
            [AssetRelation.ByteDuplicate]);

        internal static DedupPlanStatistics Statistics() => new(
            ObservedEntries: 120,
            AnalyzedFiles: 80,
            NotReadFiles: 30,
            FailedFiles: 5,
            SkippedFiles: 5,
            ByteDuplicateGroups: 1,
            ByteDuplicateFiles: 2,
            ByteDuplicateBytes: 8_192,
            ReadBytes: 10_485_760,
            AdditionalReadAttempts: 0);

        internal static DedupPlanSummary Summary() => new(
            DedupAnalysisStatus.PartiallyAnalyzed,
            ["locked/blocked.bin"],
            [DedupReadFailure.PermissionDenied],
            [DedupSkipReason.ExceedsBudget],
            ScanBoundsReached: true,
            FailureCode: null,
            [new DedupSourceFailure(new DedupSourceId(SourceId), "SkippedByBudget")]);

        internal static DedupAcceptedSource AcceptedSource() =>
            new(new DedupSourceId(SourceId), "设计素材", DedupSourceRole.RegisteredLibrary, "设计素材");
    }

    /// <summary>The plan document written by the export operation, including its refusal of authority.</summary>
    internal static class Plan
    {
        public static DedupExportDocument Exported() => new(
            FormatVersion: 1,
            DocumentType: "assetlibrary.dedup.plan",
            TaskId: TaskId.ToString("D"),
            AnalysisVersion: Version(1),
            LibraryId: LibraryId.ToString("D"),
            LibraryDisplayName: "设计素材",
            PolicyVersion: DedupContractText.PolicyVersion,
            AnalyzedAt: new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero),
            ExportedAt: new DateTimeOffset(2026, 9, 17, 9, 10, 0, TimeSpan.Zero),
            PlanDigest: "digest-4f2a9c7b",
            RetentionBoundary: DedupJobContractText.RetentionBoundary,
            GrantsFileOperation: false,
            Limits: new DedupAnalysisLimitsSnapshot(200_000, 512L * 1024 * 1024 * 1024, 64 * 1024 * 1024, 4),
            Sources: [Report.AcceptedSource()],
            RejectedSources:
            [
                new DedupRejectedSource(DedupSourceId.New(), DedupSourceRejection.ManagedLibraryOverlap, "库根"),
            ],
            Statistics: Report.Statistics(),
            Summary: Report.Summary(),
            Recheck: new DedupRecheckEvidence(false, "NotRechecked", [], 0, 0, 0, null, "digest-4f2a9c7b"),
            Groups: [Group()],
            Unverified: [],
            Unreadable: [],
            Truncated: false);

        private static DedupExportGroup Group() => new(
            "group-1",
            4096,
            "sha256-evidence",
            "完整强哈希一致",
            [
                new DedupExportMember(
                    "photos/beach.png",
                    "设计素材",
                    4096,
                    "sha256",
                    "structure",
                    new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
                    nameof(DedupItemReadState.ContentVerified),
                    DedupText.Describe(AssetRelation.ByteDuplicate)),
            ]);
    }
}
