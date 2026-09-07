using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Contracts;

public readonly record struct StableEntryId
{
    public StableEntryId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    public Guid Value { get; }

    public static StableEntryId New() => new(Guid.NewGuid());
}

public readonly record struct RelativeAssetPath
{
    public RelativeAssetPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Replace('\\', '/');
        Validate(normalized);
        Value = normalized;
    }

    public string Value { get; }

    public string Name => Value[(Value.LastIndexOf('/') + 1)..];

    private static void Validate(string value)
    {
        if (value.StartsWith('/')
            || HasWindowsDrivePrefix(value)
            || value.Contains('\0'))
        {
            throw new ArgumentException("An asset path must be relative to its library root.", nameof(value));
        }

        var segments = value.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("An asset path cannot contain empty or traversal segments.", nameof(value));
        }
    }

    private static bool HasWindowsDrivePrefix(string value) =>
        value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':';
}

public enum AssetEntryKind
{
    File = 0,
    Directory = 1,
    ReparseFile = 2,
    ReparseDirectory = 3,
}

public sealed record AssetObservation(
    StableEntryId EntryId,
    RelativeAssetPath RelativePath,
    AssetEntryKind Kind,
    long? ContentLength,
    DateTimeOffset LastWriteTimeUtc)
{
    public AssetObservation Validate()
    {
        if (EntryId.Value == Guid.Empty || string.IsNullOrWhiteSpace(RelativePath.Value))
        {
            throw new ArgumentException("An observation requires non-empty entry and relative-path identities.");
        }

        var requiresContentLength = Kind == AssetEntryKind.File;
        if ((requiresContentLength && ContentLength is null or < 0)
            || (!requiresContentLength && ContentLength is not null))
        {
            throw new ArgumentException(
                "Content length is required only for regular files and must be non-negative.");
        }

        return this;
    }
}

public interface IAssetObservationSink
{
    ValueTask<IAssetObservationSession> BeginInitialScanAsync(
        Guid scanId,
        LibraryId libraryId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);
}

public interface IAssetObservationSession : IAsyncDisposable
{
    ValueTask StageAsync(
        IReadOnlyList<AssetObservation> observations,
        CancellationToken cancellationToken);

    ValueTask<int> CompleteAsync(DateTimeOffset observedAt, CancellationToken cancellationToken);

    ValueTask AbortAsync(CancellationToken cancellationToken);
}

public sealed record InitialIndexSnapshot(Guid ScanId, int EntryCount, DateTimeOffset ObservedAt);

public interface IAssetIndexSnapshotQuery
{
    ValueTask<InitialIndexSnapshot?> FindAsync(LibraryId libraryId, CancellationToken cancellationToken);
}

public interface IInitialScanStageMaintenance
{
    ValueTask AbortAsync(Guid scanId, LibraryId libraryId, CancellationToken cancellationToken);
}
