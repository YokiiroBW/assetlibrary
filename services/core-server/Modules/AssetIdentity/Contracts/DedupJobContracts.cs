using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;

/// <summary>
/// Local administrator operation identity for one dedup job mutation. It mirrors the read-only
/// trial's management operation so an idempotent retry is recognised instead of enqueuing twice.
/// </summary>
public readonly record struct DedupOperation(Guid PrincipalId, Guid IdempotencyKey)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfEqual(PrincipalId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(IdempotencyKey, Guid.Empty);
    }
}

/// <summary>
/// The libraries a dedup job may read. The browser submits a library id and the Host resolves the
/// physical root; an id outside this set is refused before any directory is touched.
/// </summary>
public sealed record DedupLibrarySourceIds(IReadOnlyList<LibraryId> AllowedLibraryIds)
{
    public bool Contains(LibraryId libraryId) => AllowedLibraryIds.Contains(libraryId);
}

/// <summary>
/// One job's durable facts. State is owned by TaskHealth; this view only names what the browser
/// may observe, so a queued job is never reported as a finished analysis. The current plan digest
/// deliberately belongs to the results and export documents instead of here: freshness is a property
/// of the report version, and a status response must not be usable as a recheck credential.
/// </summary>
public sealed record DedupJobView(
    Guid TaskId,
    LibraryId LibraryId,
    string LibraryDisplayName,
    string State,
    bool CancellationRequested,
    bool CanCancel,
    bool CanRetry,
    bool ReportAvailable,
    string AnalysisVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? FailureCode,
    string RetentionBoundary,
    DedupAnalysisLimits Limits);

/// <summary>
/// Why a finished analysis has no readable report. A restart or an evicted entry is reported as a
/// missing report, never as an empty result set: "no duplicates" must never be a fallback answer.
/// </summary>
public enum DedupReportAvailability
{
    Available = 0,
    NotAnalyzedYet = 1,
    NotRetained = 2,
}

public enum DedupFindingKind
{
    ByteDuplicateGroup = 0,
    Unverified = 1,
    Unreadable = 2,
    Unique = 3,
}

/// <summary>
/// One observed file as the retained report holds it. Paths stay plain relative strings so the
/// report survives a JSON round trip for export and can be rebuilt into the module's value types
/// only when a recheck needs them.
/// </summary>
public sealed record DedupReportItem(
    DedupSourceId SourceId,
    string Root,
    string RelativePath,
    long Length,
    string? Sha256,
    string? StructureHash,
    DateTimeOffset LastWriteTimeUtc,
    DedupPlanItemState State,
    DedupItemReadState ReadState,
    DedupReadFailure Failure,
    DedupSkipReason SkipReason,
    string? GroupKey,
    DedupCategory Category,
    IReadOnlyList<AssetRelation> Relations)
{
    public string Name => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
}

public sealed record DedupReportGroup(
    string GroupKey,
    long Length,
    string EvidenceHash,
    bool IdentityMergeProposed,
    IReadOnlyList<DedupReportItem> Members);

/// <summary>
/// The retained, reviewable result of one bounded analysis run. It grants no file operation: every
/// item is a statement about bytes this run read or refused to read, and no member is marked as the
/// one to keep. It is not a durable record: see the retention boundary disclosed by the Host.
/// </summary>
public sealed record DedupReport
{
    public required DedupAnalysisId AnalysisId { get; init; }

    /// <summary>The durable job this report belongs to, so a cursor can be bound to one attempt.</summary>
    public required Guid TaskId { get; init; }

    public required LibraryId LibraryId { get; init; }
    public required string LibraryDisplayName { get; init; }
    public required DateTimeOffset AnalyzedAt { get; init; }
    public required string PolicyVersion { get; init; }
    public required IReadOnlyList<DedupAcceptedSource> AcceptedSources { get; init; }
    public required IReadOnlyList<DedupRejectedSource> RejectedSources { get; init; }
    public required IReadOnlyList<string> AcceptedLibraryIds { get; init; }
    public required IReadOnlyList<DedupReportGroup> Groups { get; init; }

    /// <summary>
    /// Every comparison fact this run retained, exactly once per file, in one stable order. The three
    /// display sections below are derived views that may overlap in what they show; a recheck must not
    /// be reconstructed from them, because a verified file with no duplicate peer appears in none of
    /// them and an unreadable file can appear in two. This list is what proves whether the source still
    /// holds the evidence the report claims.
    /// </summary>
    public required IReadOnlyList<DedupReportItem> Facts { get; init; }

    public required IReadOnlyList<DedupReportItem> Unverified { get; init; }
    public required IReadOnlyList<DedupReportItem> Unreadable { get; init; }
    public required DedupPlanStatistics Statistics { get; init; }
    public required DedupPlanSummary Summary { get; init; }
    public required string PlanDigest { get; init; }
    public required DedupAnalysisLimits Limits { get; init; }
    public required bool Truncated { get; init; }
    public required string RetentionBoundary { get; init; }

    public int DuplicateFileCount => Groups.Sum(group => group.Members.Count);

    public int DuplicateItemCount => Groups.Sum(group => group.Members.Count);

    public int FindingCount(DedupFindingKind kind) => kind switch
    {
        DedupFindingKind.ByteDuplicateGroup => Groups.Count,
        DedupFindingKind.Unverified => Unverified.Count,
        DedupFindingKind.Unreadable => Unreadable.Count,
        DedupFindingKind.Unique => Math.Max(Statistics.AnalyzedFiles - DuplicateItemCount, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>
/// Durable identity of one retained report: which job produced it and which lease generation wrote
/// it. A later attempt of the same job gets a higher generation, so a cursor or export bound to the
/// older generation cannot be replayed against newer results.
/// </summary>
public readonly record struct DedupReportKey(Guid TaskId, long Generation)
{
    public string VersionText => $"{TaskId:D}:{Generation}";
}

/// <summary>
/// One page of findings, bound to the exact report version it was read from. The cursor is signed
/// with a per-report secret, so a cursor from another report version is refused instead of silently
/// answering from a different result set.
/// </summary>
public sealed record DedupResultsPage(
    Guid TaskId,
    string AnalysisVersion,
    DedupFindingKind Kind,
    IReadOnlyList<DedupReportGroup> Groups,
    IReadOnlyList<DedupReportItem> Items,
    int Offset,
    int PageSize,
    int Total,
    string? NextCursor,
    bool ReportAvailable,
    DedupReportSummary Summary);

public sealed record DedupReportSummary(
    string PolicyVersion,
    DateTimeOffset AnalyzedAt,
    string PlanDigest,
    DedupPlanStatistics Statistics,
    DedupPlanSummary Plan,
    int DuplicateGroupCount,
    int DuplicateFileCount,
    int UnverifiedCount,
    int UnreadableCount,
    int UniqueCount,
    int RetainedItemCount,
    bool Truncated,
    string RetentionBoundary,
    string ExpectedVersion);

/// <summary>
/// Outcome of re-reading a retained report's sources. A recheck never edits the old report: it
/// either proves every recorded observation still matches or publishes a new version beside it.
/// </summary>
public sealed record DedupRecheckView(
    DedupRecountStatus Status,
    bool PlanStillCurrent,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> DisappearedPaths,
    IReadOnlyList<string> NewPaths,
    string PlanDigest,
    string? PreviousPlanDigest,
    string AnalysisVersion,
    Guid TaskId,
    bool ReportAvailable);

/// <summary>
/// What one recheck found, as its durable task recorded it. The report version it verified is part of
/// the value, so a caller can tell a fresh answer from one about evidence that has since been replaced.
/// </summary>
public sealed record DedupRecheckRun(
    bool Completed,
    DedupRecountStatus Status,
    bool PlanStillCurrent,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> DisappearedPaths,
    IReadOnlyList<string> NewPaths,
    string PlanDigest,
    string? PreviousPlanDigest,
    long VerifiedGeneration,
    string AnalysisVersion,
    string? FailureCode);

/// <summary>
/// How far a recheck has got. A recheck is a durable background task, so "asked for" and "answered"
/// are different states a page must be able to tell apart instead of treating a missing answer as one.
/// </summary>
public enum RecheckRunState
{
    /// <summary>The durable task has not produced an outcome yet.</summary>
    Pending = 0,

    /// <summary>The recheck ran and its outcome is the answer.</summary>
    Completed = 1,

    /// <summary>
    /// The recheck did not run to an answer: the retained version it named is gone or was replaced.
    /// It is reported as its own state, because "not checked" must never read as "nothing changed".
    /// </summary>
    Refused = 2,
}

/// <summary>
/// The answer to a recheck request. The receipt names the recheck's own durable task, and the outcome
/// carries the verified version so a caller can tell a fresh answer from one about evidence that has
/// since been replaced.
/// </summary>
public sealed record DedupRecheckState(
    RecheckRunState State,
    DedupRecheckRun? Run,
    Guid ReportTaskId,
    Guid RecheckTaskId,
    string LibraryDisplayName);

/// <summary>
/// The exportable plan document. It is a plan only: it carries the source version, the latest
/// recheck evidence and the ceilings the run was executed under, and it contains no move, copy,
/// rename or delete instruction and no confirmation token a writer could consume.
/// </summary>
public sealed record DedupExportDocument(
    int FormatVersion,
    string DocumentType,
    string TaskId,
    string AnalysisVersion,
    string LibraryId,
    string LibraryDisplayName,
    string PolicyVersion,
    DateTimeOffset AnalyzedAt,
    DateTimeOffset ExportedAt,
    string PlanDigest,
    string RetentionBoundary,
    bool GrantsFileOperation,
    DedupAnalysisLimitsSnapshot Limits,
    IReadOnlyList<DedupAcceptedSource> Sources,
    IReadOnlyList<DedupRejectedSource> RejectedSources,
    DedupPlanStatistics Statistics,
    DedupPlanSummary Summary,
    DedupRecheckEvidence Recheck,
    IReadOnlyList<DedupExportGroup> Groups,
    IReadOnlyList<DedupReportItem> Unverified,
    IReadOnlyList<DedupReportItem> Unreadable,
    bool Truncated);

public sealed record DedupAnalysisLimitsSnapshot(
    int MaximumFiles,
    long MaximumBytes,
    int MaximumFileBytes,
    int HashConcurrency);

/// <summary>
/// Latest recheck evidence shipped with the plan. "Not rechecked" is stated as its own value rather
/// than being implied by an absent field, so a plan never looks freshly verified by omission.
/// </summary>
public sealed record DedupRecheckEvidence(
    bool Performed,
    string Status,
    IReadOnlyList<string> Reasons,
    int ChangedCount,
    int DisappearedCount,
    int NewCount,
    DateTimeOffset? PerformedAt,
    string PlanDigest);

public sealed record DedupExportGroup(
    string GroupKey,
    long Length,
    string EvidenceHash,
    string EvidenceBasis,
    IReadOnlyList<DedupExportMember> Members);

public sealed record DedupExportMember(
    string RelativePath,
    string Root,
    long Length,
    string? Sha256,
    string? StructureHash,
    DateTimeOffset LastWriteTimeUtc,
    string VerificationState,
    string RelationNote);

/// <summary>
/// Numeric ceilings and the two safety sentences of this workbench. They are constants so a page, an
/// export and a test state the same boundary without copying prose between them.
/// </summary>
public static class DedupJobContractText
{
    public const int MaximumRetainedReports = 8;
    public const int MaximumRetainedItemsPerReport = 20_000;
    public const int MaximumPageSize = 200;
    public const int DefaultPageSize = 50;
    public const int MaximumTimeoutSeconds = 6 * 3600;
    public const int DefaultTimeoutSeconds = 3600;

    /// <summary>
    /// Honest statement of where a retained report lives. It is deliberately a sentence a reviewer
    /// can read in the UI, not an internal note: a report that disappears on restart must say so.
    /// </summary>
    public const string RetentionBoundary =
        "分析结果保存在当前服务进程内，服务重启后需要重新分析；任务状态与取消仍由持久任务系统保存。";

    public const string ReadOnlyBoundary =
        "本页只读取已登记库的字节并给出复核计划，不移动、复制、重命名或删除任何文件，也不提供执行入口。";
}

/// <summary>
/// The vocabulary of the dedup workbench. Every browser-facing phrase that states a fact about file
/// safety comes from here, so an adapter cannot render "not read" as "unique".
/// </summary>
public static class DedupText
{
    public static string Describe(DedupFindingKind kind) => kind switch
    {
        DedupFindingKind.ByteDuplicateGroup => "字节重复组",
        DedupFindingKind.Unverified => "未验证内容",
        DedupFindingKind.Unreadable => "本次不可读",
        DedupFindingKind.Unique => "暂未发现同长候选",
        _ => "未知分类",
    };

    public static string Describe(DedupItemReadState state) => state switch
    {
        DedupItemReadState.ContentVerified => "已用完整强哈希验证内容",
        DedupItemReadState.NotRead => "本次未读取内容",
        DedupItemReadState.ReadFailed => "本次读取失败",
        DedupItemReadState.SkippedByBudget => "本次因预算未读取",
        _ => "未知读取状态",
    };

    public static string Describe(DedupPlanItemState state) => state switch
    {
        DedupPlanItemState.Analyzed => "已分析",
        DedupPlanItemState.NotAnalyzed => "未分析内容",
        DedupPlanItemState.Unreadable => "不可读",
        _ => "未知状态",
    };

    public static string Describe(DedupRecountStatus status) => status switch
    {
        DedupRecountStatus.Identical => "来源未变化",
        DedupRecountStatus.SourceChanged => "来源已变化",
        DedupRecountStatus.UnreadableNow => "来源当前不可读",
        DedupRecountStatus.Disappeared => "部分文件已消失",
        DedupRecountStatus.NewContent => "出现新的内容",
        DedupRecountStatus.Timeout => "重新核对超时",
        _ => "未知核对状态",
    };

    public static string Describe(DedupRecountReason reason) => reason switch
    {
        DedupRecountReason.ContentChanged => "文件内容已变化",
        DedupRecountReason.MetadataChanged => "文件元数据已变化",
        DedupRecountReason.SourceAvailabilityChanged => "来源可用性发生变化",
        DedupRecountReason.PermissionDenied => "读取权限被拒绝",
        DedupRecountReason.SourceMissing => "来源不再存在",
        DedupRecountReason.SourceRejected => "来源被拒绝",
        DedupRecountReason.NewFileObserved => "观察到新的文件",
        DedupRecountReason.ScanIncomplete => "扫描不完整",
        DedupRecountReason.ContentUnverified => "内容未经验证",
        DedupRecountReason.SnapshotTruncated => "保留的证据不完整，本次对照只覆盖报告保留的部分",
        _ => "未说明原因",
    };

    public static string Describe(AssetRelation relation) => relation switch
    {
        AssetRelation.None => "无关系",
        AssetRelation.ByteDuplicate => "完整强哈希相同",
        AssetRelation.SameNameDifferentContent => "同名但内容不同",
        AssetRelation.SameLengthDifferentContent => "同长但内容不同",
        AssetRelation.CompanionFile => "附属文件",
        AssetRelation.AnimatedVariant => "动画版本差异",
        AssetRelation.EncodingVariant => "编码版本差异",
        AssetRelation.RevisionVariant => "修订版本差异",
        _ => "未知关系",
    };

    public static string Describe(DedupAnalysisStatus status) => status switch
    {
        DedupAnalysisStatus.Completed => "已完成",
        DedupAnalysisStatus.PartiallyAnalyzed => "部分完成",
        DedupAnalysisStatus.SourceRejected => "来源被拒绝",
        DedupAnalysisStatus.Empty => "来源没有条目",
        DedupAnalysisStatus.TimedOut => "已超时",
        DedupAnalysisStatus.Cancelled => "已取消",
        DedupAnalysisStatus.Failed => "已失败",
        _ => "未知分析状态",
    };
}

/// <summary>Hard ceilings for one job execution, shared by the analyzer and its lease heartbeat.</summary>
public sealed record DedupExecutionOptions
{
    public TimeSpan AnalysisTimeout { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RecheckTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The TaskHealth task type this workbench owns. It is a dedup contract because the generic task
    /// module must not know what a duplicate group is; it only routes work of this named type.
    /// </summary>
    public static TaskTypeName TaskType => new("dedup.read_only_analysis");
}
