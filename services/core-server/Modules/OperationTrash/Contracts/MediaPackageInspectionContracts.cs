using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Contracts;

/// <summary>
/// One bounded diagnostic. The location is either a manifest-relative file path or a manifest field
/// name; never an absolute path, an exception text or file content.
/// </summary>
public sealed record MediaPackageIssue(string Code, string? Location);

public enum MediaPackageInspectionStatus
{
    Inspected = 0,
    Rejected = 1,
}

/// <summary>
/// One package file whose bytes were really read and hashed during this preflight.
/// </summary>
public sealed record MediaPackageInspectedFile(
    string Path,
    MediaPackageFileKind Kind,
    string? Cid,
    PayloadFacts Payload);

/// <summary>
/// Result of the isolated real-file inspection. It deliberately carries no absolute path, no plan and
/// no execution right.
/// </summary>
public sealed record MediaPackageInspectionEvidence(
    IReadOnlyList<MediaPackageInspectedFile> Files,
    long VerifiedBytes);

/// <summary>
/// Read-only preflight report. It never grants a file operation: a later publication stage must run
/// its own preflight and its own authorization.
/// </summary>
public sealed record MediaPackagePreflightReport(
    MediaPackageInspectionStatus Status,
    bool GrantsFileOperation,
    string? PackageId,
    Sha256Digest ManifestDigest,
    string? ScopeRevision,
    DateTimeOffset ObservedAt,
    int VerifiedFileCount,
    long VerifiedBytes,
    IReadOnlyList<MediaPackageIssue> Issues,
    bool IssuesTruncated)
{
    /// <summary>
    /// Every report states the same unverified dimensions; the list is a frozen contract, not a
    /// per-run observation, so it is exposed on the report itself even though it needs no run state.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The frozen unverified list is part of the report's shape: callers read it from the report they already hold, so making it static would move a contract member off the contract type.")]
    public IReadOnlyList<string> Unverified => MediaPackageUnverifiedFacts.All;
}

/// <summary>
/// Input of the single preflight entry point. The manifest flows as raw bytes because the request
/// digest is defined over exactly those bytes.
/// </summary>
public sealed record MediaPackagePreflightRequest(
    MediaPackageCallerContext Caller,
    byte[] ManifestBytes,
    Sha256Digest ExpectedManifestDigest);

/// <summary>
/// Outcome of reading a manifest from raw bytes. A named failure replaces any parser or path detail,
/// so callers never observe a raw <c>JsonException</c> or an absolute path. The truncation flag is part
/// of the frozen report semantics and is therefore carried out of the reader rather than recomputed.
/// </summary>
public sealed record MediaPackageManifestReadResult(
    MediaPackageManifest? Manifest,
    Sha256Digest ManifestDigest,
    string? FailureCode,
    IReadOnlyList<MediaPackageIssue> Issues,
    bool IssuesTruncated)
{
    public bool Succeeded => Manifest is not null && FailureCode is null;
}

/// <summary>
/// Strict raw-byte manifest reader: shape and cross-field policy plus the digest of exactly the
/// received bytes (no LF normalization, no reserialization).
/// </summary>
public interface IMediaPackageManifestReader
{
    MediaPackageManifestReadResult Read(byte[] manifestBytes, Sha256Digest expectedDigest);
}

/// <summary>
/// Trusted read-only source of the staging package root, the target library root, the authorization
/// revision and availability. Host adapters implement it; this task ships no production adapter.
/// </summary>
public interface IMediaPackageInspectionScopeQuery
{
    /// <summary>
    /// Resolves the scope for one trusted caller. Deny-by-default: a missing or revoked registration
    /// returns null and the caller must not touch any directory or file.
    /// </summary>
    ValueTask<MediaPackageInspectionScope?> ResolveAsync(
        MediaPackageCallerContext caller,
        string stagingRef,
        LibraryId libraryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-verifies that a previously resolved scope is still permitted, still registered at the same
    /// revision and not expired.
    /// </summary>
    ValueTask<bool> IsCurrentAsync(
        MediaPackageInspectionScope scope,
        CancellationToken cancellationToken);
}
