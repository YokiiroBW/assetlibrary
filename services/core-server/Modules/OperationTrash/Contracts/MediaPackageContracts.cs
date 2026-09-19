using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Contracts;

/// <summary>
/// Physical layout of a prepared inbound media package (contracts/media-package/candidate-v1).
/// </summary>
public enum MediaPackageLayout
{
    SinglePart = 0,
    Multipart = 1,
}

public enum MediaPackageFileKind
{
    Video = 0,
    Nfo = 1,
    Source = 2,
    Poster = 3,
    EpisodeThumb = 4,
}

/// <summary>
/// One selected part of the source item. The episode number is null only for the single layout.
/// </summary>
public sealed record MediaPackageSelectedPart(string Cid, int? EpisodeNumber);

/// <summary>
/// One declared package file. The path is the raw POSIX relative path as written in the manifest; it is
/// never normalized, because normalizing would silently accept traversal or separator abuse. The cid is
/// null exactly when the entry is not scoped to one part.
/// </summary>
public sealed record MediaPackageFileEntry(
    string Path,
    MediaPackageFileKind Kind,
    string? Cid,
    long SizeBytes,
    Sha256Digest Sha256);

/// <summary>
/// A manifest that passed the full shape, identity, layout, path and file-set policy. Only such an
/// instance may reach authorization and filesystem inspection.
/// </summary>
public sealed record MediaPackageManifest(
    string PackageId,
    string StagingRef,
    LibraryId LibraryId,
    string Bvid,
    MediaPackageLayout Layout,
    string MediaExtension,
    IReadOnlyList<MediaPackageSelectedPart> SelectedParts,
    IReadOnlyList<MediaPackageFileEntry> Files);

/// <summary>
/// Trusted caller identity resolved by the host, never by the manifest body. The manifest cannot
/// self-declare a library or an authorization.
/// </summary>
public sealed record MediaPackageCallerContext(string CallerId);

/// <summary>
/// Absolute root of a trusted staging package. The value exists only inside a trusted port and inside
/// the infrastructure boundary; it never reaches a report.
/// </summary>
public readonly record struct MediaPackageStagingRoot
{
    public MediaPackageStagingRoot(string value, RootPathComparison comparison)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\0') || value.TrimEnd('\\', '/').Length == 0)
        {
            throw new ArgumentException(
                "A staging root must be a non-empty absolute path.",
                nameof(value));
        }

        Value = value;
        Comparison = comparison;
    }

    public string Value { get; }

    public RootPathComparison Comparison { get; }
}

/// <summary>
/// Trusted read-only scope for one staging package and one target library, resolved by a trusted port.
/// Absolute paths stay inside the port; they never reach a report.
/// </summary>
public sealed record MediaPackageInspectionScope(
    string Revision,
    DateTimeOffset ExpiresAt,
    StorageAvailability Availability,
    MediaPackageStagingRoot StagingRoot,
    MediaPackageStagingRoot TargetLibraryRoot);

/// <summary>
/// Source of truth for the frozen error vocabulary of the read-only preflight report.
/// </summary>
public static class MediaPackagePreflightCodes
{
    public const string InvalidManifest = "invalid_manifest";
    public const string DigestMismatch = "digest_mismatch";
    public const string UnsupportedVersion = "unsupported_version";
    public const string InvalidIdentity = "invalid_identity";
    public const string InvalidLayout = "invalid_layout";
    public const string InvalidPath = "invalid_path";
    public const string DuplicatePath = "duplicate_path";
    public const string InvalidFileSet = "invalid_file_set";
    public const string BudgetExceeded = "budget_exceeded";
    public const string Unauthorized = "unauthorized";
    public const string ScopeChanged = "scope_changed";
    public const string SourceMissing = "source_missing";
    public const string SourceChanged = "source_changed";
    public const string UnsafePath = "unsafe_path";
    public const string HashMismatch = "hash_mismatch";
    public const string SizeMismatch = "size_mismatch";
    public const string TargetExists = "target_exists";
    public const string TargetUnavailable = "target_unavailable";
    public const string InsufficientSpace = "insufficient_space";
    public const string Busy = "busy";
    public const string Timeout = "timeout";
    public const string IoFailure = "io_failure";

    /// <summary>
    /// Every code the preflight report may carry, in the order fixed by the candidate semantics.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        InvalidManifest,
        DigestMismatch,
        UnsupportedVersion,
        InvalidIdentity,
        InvalidLayout,
        InvalidPath,
        DuplicatePath,
        InvalidFileSet,
        BudgetExceeded,
        Unauthorized,
        ScopeChanged,
        SourceMissing,
        SourceChanged,
        UnsafePath,
        HashMismatch,
        SizeMismatch,
        TargetExists,
        TargetUnavailable,
        InsufficientSpace,
        Busy,
        Timeout,
        IoFailure,
    ];
}

/// <summary>
/// Facts the preflight must always report as unverified, because this task only proves bytes, paths,
/// authorization and budget.
/// </summary>
public static class MediaPackageUnverifiedFacts
{
    public const string MediaDecoding = "media_decoding";
    public const string QualityPolicy = "quality_policy";
    public const string MetadataSemantics = "metadata_semantics";
    public const string Publication = "publication";
    public const string Indexing = "indexing";
    public const string MediaServerImport = "media_server_import";
    public const string ProductionPathRaces = "production_path_races";

    public static IReadOnlyList<string> All { get; } =
    [
        MediaDecoding,
        QualityPolicy,
        MetadataSemantics,
        Publication,
        Indexing,
        MediaServerImport,
        ProductionPathRaces,
    ];
}

/// <summary>
/// Bounded, ordered diagnostic sink shared by the manifest reader, the policy and the inspector. It
/// keeps at most one entry per distinct code and location pair, so a hostile package cannot inflate
/// the report; the first occurrence of each pair fixes the report order. The frozen cap is enforced
/// while recording.
/// </summary>
public sealed class MediaPackageIssueSink
{
    private readonly List<MediaPackageIssue> issues = [];
    private readonly HashSet<(string Code, string? Location)> recorded = [];
    private readonly HashSet<string> codes = new(StringComparer.Ordinal);

    public MediaPackageIssueSink(int maximumIssues)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumIssues);
        MaximumIssues = maximumIssues;
    }

    public int MaximumIssues { get; }

    public bool IsTruncated { get; private set; }

    public IReadOnlyList<MediaPackageIssue> Issues => issues;

    public IReadOnlyCollection<string> Codes => codes;

    public bool HasIssues => issues.Count > 0;

    public bool IsEmpty => issues.Count == 0 && !IsTruncated;

    public bool Record(string code, string? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        codes.Add(code);
        if (!recorded.Add((code, location)))
        {
            return false;
        }

        if (issues.Count >= MaximumIssues)
        {
            IsTruncated = true;
            return false;
        }

        issues.Add(new MediaPackageIssue(code, location));
        return true;
    }

    /// <summary>
    /// Carries the issues of an earlier phase into a later phase without duplicating entries.
    /// </summary>
    public void Absorb(MediaPackageIssueSink other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var issue in other.Issues)
        {
            Record(issue.Code, issue.Location);
        }

        if (other.IsTruncated)
        {
            IsTruncated = true;
        }
    }
}
