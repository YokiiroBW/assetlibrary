using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Application;

/// <summary>
/// One package file whose bytes were really read and hashed, with the facts the policy needs to bind
/// the observation to the declaration.
/// </summary>
public sealed record MediaPackageObservedFile(
    string Path,
    MediaPackageFileKind Kind,
    string? Cid,
    PayloadFacts Payload);

/// <summary>
/// Result of the isolated real-file inspection. The issue sink carries the named failures; the
/// inspector never throws for a package-level failure and never returns a raw exception.
/// </summary>
public sealed record MediaPackageInspectionOutcome(
    MediaPackageInspectionScope Scope,
    IReadOnlyList<MediaPackageObservedFile> Files,
    long VerifiedBytes,
    int EnumeratedEntries,
    MediaPackageIssueSink Issues)
{
    public bool Succeeded => Files.Count > 0 && Issues.IsEmpty;
}

/// <summary>
/// Real isolated file inspection of an already-authorized package. Implementations resolve no scope
/// themselves: authorization stays an explicit input so the inspector cannot widen its own rights.
/// </summary>
public interface IMediaPackageInspectionPort
{
    ValueTask<MediaPackageInspectionOutcome> InspectAsync(
        MediaPackageInspectionScope scope,
        MediaPackageManifest manifest,
        MediaPackageInspectionLimits limits,
        CancellationToken cancellationToken);
}

/// <summary>
/// Narrow observation of the free space of the volume that holds a directory. It exists so a space
/// failure is testable without inventing a global storage abstraction.
/// </summary>
public interface IMediaPackageVolumeSpaceObserver
{
    /// <summary>
    /// Returns the free bytes of the volume holding <paramref name="directory"/>, or null when the
    /// volume cannot be observed. Null must be reported as target_unavailable, never guessed.
    /// </summary>
    ValueTask<long?> ObserveAvailableBytesAsync(
        string directory,
        CancellationToken cancellationToken);
}

/// <summary>
/// Raised by a reader when the bytes it was allowed to read would exceed the remaining read budget. It
/// derives from <see cref="IOException"/> so every existing environment-failure handler still catches it,
/// while the inspection port can translate it into the named <c>budget_exceeded</c> verdict instead of
/// the generic <c>io_failure</c>.
/// </summary>
public sealed class MediaPackageBudgetExceededException : IOException
{
    public MediaPackageBudgetExceededException()
        : base("The package file exceeds the remaining read budget.")
    {
    }

    public MediaPackageBudgetExceededException(string message)
        : base(message)
    {
    }

    public MediaPackageBudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Bounded asynchronous reader over the package files so the inspector enforces the byte budget while
/// streaming, instead of trusting a declared length.
/// </summary>
public interface IMediaPackageFileHasher
{
    /// <summary>
    /// Reads the whole file through a bounded buffer and returns its exact length and SHA-256. Throws
    /// <see cref="MediaPackageBudgetExceededException"/> when the file exceeds
    /// <paramref name="byteLimit"/> and <see cref="UnauthorizedAccessException"/> when the file cannot be
    /// opened.
    /// </summary>
    ValueTask<PayloadFacts> HashAsync(
        string absolutePath,
        long byteLimit,
        CancellationToken cancellationToken);
}
