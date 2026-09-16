using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;

/// <summary>
/// Entry point of one read-only duplicate candidate analysis over already registered libraries.
/// The request never carries a destination, an execution right or a deletion intent: this slice
/// only reports what the registered sources currently contain and what a human could review.
/// </summary>
public sealed record DedupAnalysisRequest(
    DedupAnalysisId AnalysisId,
    IReadOnlyList<DedupSourceRequest> Sources,
    DedupAnalysisLimits Limits,
    TimeSpan Timeout);

public readonly record struct DedupAnalysisId
{
    [JsonConstructor]
    public DedupAnalysisId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    public Guid Value { get; }

    public static DedupAnalysisId New() => new(Guid.NewGuid());
}

public enum DedupSourceRole
{
    /// <summary>An already registered library root that the index itself owns.</summary>
    RegisteredLibrary = 0,

    /// <summary>An explicitly isolated inbound directory awaiting curation, never a managed output.</summary>
    InboundStaging = 1,
}

public sealed record DedupSourceRequest(
    DedupSourceId SourceId,
    string DisplayName,
    DedupSourceRole Role,
    LibraryId LibraryId,
    CanonicalLibraryRoot Root);

public readonly record struct DedupSourceId
{
    [JsonConstructor]
    public DedupSourceId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    public Guid Value { get; }

    public static DedupSourceId New() => new(Guid.NewGuid());
}

/// <summary>
/// Hard ceilings for one analysis run. They exist so a 500k asset library cannot silently turn a
/// preview into an unbounded read: work that exceeds a ceiling is reported, never inferred.
/// </summary>
public sealed record DedupAnalysisLimits(
    int MaximumFiles,
    long MaximumBytes,
    int MaximumFileBytes,
    int HashConcurrency = 4)
{
    public const int DefaultMaximumFiles = 200_000;

    public const long DefaultMaximumBytes = 512L * 1024 * 1024 * 1024;

    public const int DefaultMaximumFileBytes = 64 * 1024 * 1024;

    public static DedupAnalysisLimits Default { get; } = new(
        DefaultMaximumFiles,
        DefaultMaximumBytes,
        DefaultMaximumFileBytes);

    public DedupAnalysisLimits Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(HashConcurrency);
        if (HashConcurrency > 32)
        {
            // A background analyzer must not monopolise a NAS link or the host CPU.
            throw new ArgumentOutOfRangeException(
                nameof(HashConcurrency),
                "Hash concurrency is capped at 32 parallel reads.");
        }

        return this;
    }
}

/// <summary>
/// A durable, comparable description of one file as this analysis observed it. It is built only
/// from bytes this run actually read plus the metadata captured in the same run, so it can later
/// prove whether the source changed instead of trusting a path.
/// </summary>
public sealed record ContentSignature(
    long Length,
    Sha256Digest Sha256,
    long StructureHash,
    DateTimeOffset LastWriteTimeUtc,
    string SourceIdentity)
{
    /// <summary>
    /// The evidence key. Equal keys mean every fact this analysis observed is identical; any
    /// difference is treated as source drift even when the strong hash still matches.
    /// </summary>
    public string IdentityKey =>
        $"{Length}|{Sha256.Value}|{StructureHash:X16}|{LastWriteTimeUtc.UtcTicks}|{SourceIdentity}";

    public static string BuildSourceIdentity(CanonicalLibraryRoot root, RelativeAssetPath relativePath) =>
        $"{root.Value}|{relativePath.Value}";
}

public enum DedupReadFailure
{
    None = 0,
    PermissionDenied = 1,
    Missing = 2,
    Unreadable = 3,
    ChangedDuringRead = 4,
    UnsafePath = 5,
    SourceHasNoReparsePointFreeParents = 6,
    TooLarge = 7,
}

/// <summary>
/// Result of reading one file's bytes. A failure is a statement about readability, never a claim
/// that the source no longer exists and never a reason to drop a previously known asset.
/// </summary>
public sealed record ContentReadResult(
    RelativeAssetPath RelativePath,
    long Length,
    string? Sha256,
    long StructureHash,
    DateTimeOffset LastWriteTimeUtc,
    DedupReadFailure Failure);

public enum DedupAnalysisStatus
{
    Completed = 0,
    PartiallyAnalyzed = 1,
    SourceRejected = 2,
    Empty = 3,
    TimedOut = 4,
    Cancelled = 5,
    Failed = 6,
}

public enum DedupSourceRejection
{
    None = 0,

    /// <summary>The root is already a registered managed library root: it cannot be taken over as inbound.</summary>
    ManagedLibraryOverlap = 1,

    /// <summary>The root is where this product publishes managed output, so reading it would re-import its own output.</summary>
    OutputBackflow = 2,

    /// <summary>The same physical directory was submitted twice in one request.</summary>
    DuplicateSourceRegistration = 3,

    /// <summary>The submitted root is not a physical absolute directory reference.</summary>
    SourceRootInvalid = 4,

    /// <summary>The registered library root is unknown to this installation.</summary>
    RegisteredLibraryUnknown = 5,

    /// <summary>
    /// The source exists in the request but cannot be read right now (an offline share, a device
    /// that is not mounted, or a root that is itself a reparse point). Its files are not deleted.
    /// </summary>
    SourceUnavailable = 6,
}

public enum DedupItemReadState
{
    ContentVerified = 0,
    NotRead = 1,
    ReadFailed = 2,
    SkippedByBudget = 3,
}

public enum DedupSkipReason
{
    None = 0,

    /// <summary>Another file of the same size exists, but this one is outside the file/byte budget.</summary>
    ExceedsBudget = 1,

    /// <summary>Only this file has its content length, so no byte comparison can be needed.</summary>
    NoLengthCandidate = 2,

    /// <summary>A reparse point is reported as a distinct entity and never followed or read as content.</summary>
    ReparsePoint = 3,

    /// <summary>Shared discovery exclusions (thumbnail caches, partial downloads, temp files).</summary>
    ExcludedByDiscoveryPolicy = 4,
}

public enum AssetRelation
{
    None = 0,

    /// <summary>Byte-identical content proven by the same complete strong hash, size and structure hash.</summary>
    ByteDuplicate = 1,

    /// <summary>Filenames are equal but content differs. This is not duplicate evidence.</summary>
    SameNameDifferentContent = 2,

    /// <summary>Equal content length with different content. Not duplicate evidence either.</summary>
    SameLengthDifferentContent = 3,

    /// <summary>A companion file such as a sidecar, description or metadata document.</summary>
    CompanionFile = 4,

    /// <summary>A still version and an animated version of a related asset (for example PNG and APNG/GIF).</summary>
    AnimatedVariant = 5,

    /// <summary>Two representations of the same asset in different container formats.</summary>
    EncodingVariant = 6,

    /// <summary>A second revision of the same relative name in another directory or with a revision marker.</summary>
    RevisionVariant = 7,
}

/// <summary>
/// Ordered categories a curator can group by. It intentionally carries no "preferred" or
/// "deletable" member: this slice never decides what may be removed.
/// </summary>
public enum DedupCategory
{
    Unique = 0,
    ByteDuplicateGroup = 1,
    SameNameDifferentContent = 2,
    SameLengthDifferentContent = 3,
    CompanionOrVariant = 4,
    Unreadable = 5,
}

public enum DedupPlanItemState
{
    Analyzed = 0,
    NotAnalyzed = 1,
    Unreadable = 2,
}

public sealed record DedupPlanItem(
    string SourceIdentity,
    DedupSourceId SourceId,
    long Length,
    string? Sha256,
    long StructureHash,
    DateTimeOffset LastWriteTimeUtc,
    DedupPlanItemState State,
    DedupItemReadState ReadState,
    DedupReadFailure Failure,
    DedupSkipReason SkipReason,
    string? GroupKey,
    DedupCategory Category,
    IReadOnlyList<AssetRelation> Relations)
{
    /// <summary>
    /// The path relative to its source root, recovered from the identity rather than stored twice.
    /// Keeping both a struct and a string here made the preview lossy through a JSON round trip,
    /// and a preview that cannot be stored and re-read cannot be reviewed later.
    /// </summary>
    public string PathText =>
        SourceIdentity[(SourceIdentity.IndexOf('|', StringComparison.Ordinal) + 1)..];

    /// <summary>The file name only, for display.</summary>
    public string NameText =>
        PathText[(PathText.LastIndexOf('/') + 1)..];
}

public sealed record DedupPlanGroup(
    string GroupKey,
    long Length,
    IReadOnlyList<string> MemberIdentities,
    AssetRelation Evidence,
    bool IdentityMergeProposed);

public sealed record DedupPlanStatistics(
    int ObservedEntries,
    int AnalyzedFiles,
    int NotReadFiles,
    int FailedFiles,
    int SkippedFiles,
    int ByteDuplicateGroups,
    int ByteDuplicateFiles,
    long ByteDuplicateBytes,
    long ReadBytes,
    int AdditionalReadAttempts);

public sealed record DedupPlanSummary(
    DedupAnalysisStatus Status,
    IReadOnlyList<string> UnreadablePaths,
    IReadOnlyList<DedupReadFailure> UnreadableReasons,
    IReadOnlyList<DedupSkipReason> IncompleteReasons,
    bool ScanBoundsReached,
    string? FailureCode,
    IReadOnlyList<DedupSourceFailure> SourceFailures);

/// <summary>
/// A source this run could not traverse completely. An aborted traversal is an incomplete scan and
/// must never be reported as a source whose files disappeared.
/// </summary>
public sealed record DedupSourceFailure(DedupSourceId SourceId, string ReasonCode);

/// <summary>
/// One source a preview actually read, recorded in the plan's own vocabulary so a stored preview
/// round-trips exactly. Nested value types from other modules cannot be recovered from JSON, and a
/// preview that cannot be re-read cannot be reviewed later.
/// </summary>
public sealed record DedupAcceptedSource(
    DedupSourceId SourceId,
    string DisplayName,
    DedupSourceRole Role,
    string Root);

/// <summary>Why one submitted source was refused before any byte was read.</summary>
public sealed record DedupRejectedSource(
    DedupSourceId SourceId,
    DedupSourceRejection Rejection,
    string Root);

/// <summary>
/// A serializable, reviewable preview. It grants no file operation: there is no execution entry
/// point, no confirmation digest and no path a caller could hand to a writer in this slice.
/// </summary>
public sealed record DedupCurationPlan(
    DedupAnalysisId AnalysisId,
    DateTimeOffset AnalyzedAt,
    string PolicyVersion,
    IReadOnlyList<DedupAcceptedSource> AcceptedSources,
    IReadOnlyList<DedupRejectedSource> RejectedSources,
    IReadOnlyList<DedupPlanItem> Items,
    IReadOnlyList<DedupPlanGroup> Groups,
    DedupPlanStatistics Statistics,
    DedupPlanSummary Summary,
    string PlanDigest)
{
    /// <summary>Honest statement of scope: this object can only be read.</summary>
    public static bool GrantsFileOperation => false;
}

public enum DedupRecountStatus
{
    Identical = 0,
    SourceChanged = 1,
    UnreadableNow = 2,
    Disappeared = 3,
    NewContent = 4,
    Timeout = 5,
}

public enum DedupRecountReason
{
    None = 0,
    SourceAvailabilityChanged = 1,
    ContentChanged = 2,
    MetadataChanged = 3,
    PermissionDenied = 4,
    SourceMissing = 5,
    SourceRejected = 6,
    NewFileObserved = 7,
    ScanIncomplete = 8,
}

public sealed record DedupRecountRequest(
    DedupCurationPlan Plan,
    IReadOnlyList<DedupSourceRequest> Sources,
    TimeSpan Timeout);

public sealed record DedupRecountResult(
    DedupAnalysisId AnalysisId,
    DedupRecountStatus Status,
    IReadOnlyList<DedupRecountReason> Reasons,
    IReadOnlyList<RelativeAssetPath> ChangedPaths,
    IReadOnlyList<RelativeAssetPath> DisappearedPaths,
    IReadOnlyList<RelativeAssetPath> NewPaths,
    string PlanDigest,
    string? CurrentPlanDigest,
    DedupCurationPlan? CurrentPlan)
{
    /// <summary>True only when nothing this plan recorded changed, so a reviewer can still honour it.</summary>
    public bool PlanStillCurrent => Status == DedupRecountStatus.Identical;
}

/// <summary>
/// Textual contract of the read-only dedup slice. It is documentation for the reason codes above,
/// so a consumer cannot misread "not read" as "deleted" or "readable" as "unique".
/// </summary>
public static class DedupContractText
{
    public const string PolicyVersion = "dedup-readonly/v1";

    public static string Describe(DedupReadFailure failure) => failure switch
    {
        DedupReadFailure.None => "content_read",
        DedupReadFailure.PermissionDenied => "permission_denied",
        DedupReadFailure.Missing => "source_missing",
        DedupReadFailure.Unreadable => "source_unreadable",
        DedupReadFailure.ChangedDuringRead => "source_changed_during_read",
        DedupReadFailure.UnsafePath => "path_escape_rejected",
        DedupReadFailure.SourceHasNoReparsePointFreeParents => "path_reparse_escape_rejected",
        DedupReadFailure.TooLarge => "file_exceeds_per_file_ceiling",
        _ => "unknown_read_failure",
    };

    public static string Describe(DedupSkipReason reason) => reason switch
    {
        DedupSkipReason.None => "not_skipped",
        DedupSkipReason.ExceedsBudget => "analysis_budget_exhausted",
        DedupSkipReason.NoLengthCandidate => "no_same_length_candidate",
        DedupSkipReason.ReparsePoint => "reparse_point_not_content",
        DedupSkipReason.ExcludedByDiscoveryPolicy => "excluded_by_discovery_policy",
        _ => "unknown_skip_reason",
    };

    public static string Describe(DedupSourceRejection rejection) => rejection switch
    {
        DedupSourceRejection.None => "accepted",
        DedupSourceRejection.ManagedLibraryOverlap => "root_is_registered_library",
        DedupSourceRejection.OutputBackflow => "root_is_managed_output",
        DedupSourceRejection.DuplicateSourceRegistration => "duplicate_source_registration",
        DedupSourceRejection.SourceRootInvalid => "source_root_invalid",
        DedupSourceRejection.RegisteredLibraryUnknown => "registered_library_unknown",
        DedupSourceRejection.SourceUnavailable => "source_unavailable",
        _ => "unknown_source_rejection",
    };
}
