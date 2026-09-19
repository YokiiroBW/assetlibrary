namespace AssetLibrary.Modules.OperationTrash.Application;

/// <summary>
/// Immutable read budgets of one preflight instance. Every value has a hard contract ceiling and can
/// only be lowered by a trusted constructor; a manifest or a caller can never raise one.
/// </summary>
public sealed record MediaPackageInspectionLimits
{
    public const int MaximumConcurrentInspections = 1;
    public const int MaximumManifestBytes = 1_048_576;
    public const int MaximumFiles = 512;
    public const long MaximumReadBytes = 34_359_738_368;
    public const int MaximumStreamBufferBytes = 1_048_576;
    public const long MinimumTargetHeadroomBytes = 67_108_864;
    public const int MaximumIssues = 100;
    public const int MaximumEnumeratedEntries = 4096;

    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(120);

    public MediaPackageInspectionLimits(
        int maximumManifestBytes = MaximumManifestBytes,
        int maximumFiles = MaximumFiles,
        long maximumReadBytes = MaximumReadBytes,
        TimeSpan? maximumDuration = null,
        int streamBufferBytes = 131_072,
        long minimumTargetHeadroomBytes = MinimumTargetHeadroomBytes,
        int maximumIssues = MaximumIssues)
    {
        if (maximumManifestBytes is < 1 or > MaximumManifestBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumManifestBytes));
        }

        if (maximumFiles is < 1 or > MaximumFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFiles));
        }

        if (maximumReadBytes is < 1 or > MaximumReadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumReadBytes));
        }

        var duration = maximumDuration ?? MaximumDuration;
        if (duration <= TimeSpan.Zero || duration > MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }

        if (streamBufferBytes is < 1 or > MaximumStreamBufferBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(streamBufferBytes));
        }

        // The 64 MiB headroom is a floor, not a maximum that may be relaxed: a trusted constructor can
        // only raise it, never lower it towards zero.
        if (minimumTargetHeadroomBytes < MinimumTargetHeadroomBytes
            || minimumTargetHeadroomBytes > long.MaxValue - MaximumReadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumTargetHeadroomBytes));
        }

        if (maximumIssues is < 1 or > MaximumIssues)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumIssues));
        }

        MaximumManifestByteCount = maximumManifestBytes;
        MaximumFileCount = maximumFiles;
        MaximumReadByteCount = maximumReadBytes;
        MaximumInspectionDuration = duration;
        StreamBufferByteCount = streamBufferBytes;
        MinimumTargetHeadroomByteCount = minimumTargetHeadroomBytes;
        MaximumIssueCount = maximumIssues;
    }

    public int MaximumManifestByteCount { get; }

    public int MaximumFileCount { get; }

    public long MaximumReadByteCount { get; }

    public TimeSpan MaximumInspectionDuration { get; }

    public int StreamBufferByteCount { get; }

    public long MinimumTargetHeadroomByteCount { get; }

    public int MaximumIssueCount { get; }
}
