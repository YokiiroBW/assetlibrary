using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Contracts;

public readonly record struct AuthenticatedSubject
{
    public const int MaximumLength = 200;

    public AuthenticatedSubject(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("An authenticated subject is invalid.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public readonly record struct BrowseParentPath
{
    public BrowseParentPath(string? value)
    {
        var normalized = (value ?? string.Empty).Replace('\\', '/');
        if (normalized.Length > 4096)
        {
            throw new ArgumentException("A browse parent path is too long.", nameof(value));
        }

        if (normalized.Length > 0)
        {
            _ = new RelativeAssetPath(normalized);
        }

        Value = normalized;
    }

    public string Value { get; }
}

public readonly record struct ReadPageCursor
{
    public const int MaximumLength = 8192;

    public ReadPageCursor(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumLength || value.Any(character => !IsBase64Url(character)))
        {
            throw new ArgumentException("A read-page cursor is invalid.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    private static bool IsBase64Url(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '_';
}

public readonly record struct AssetSearchText
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 200;

    public AssetSearchText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length is < MinimumLength or > MaximumLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Search text must contain between 2 and 200 visible characters.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }
}

public readonly record struct ReadPageOptions
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(10);

    public ReadPageOptions(int pageSize, TimeSpan timeout, ReadPageCursor? cursor = null)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        if (timeout < TimeSpan.FromMilliseconds(100) || timeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        PageSize = pageSize;
        Timeout = timeout;
        Cursor = cursor;
    }

    public int PageSize { get; }

    public TimeSpan Timeout { get; }

    public ReadPageCursor? Cursor { get; }

    public static ReadPageOptions Default => new(DefaultPageSize, DefaultTimeout);
}

public sealed record AuthorizedLibrary(
    LibraryId LibraryId,
    string DisplayName,
    StorageAvailability Availability,
    LibraryAccessLevel AccessLevel);

public sealed record ReadOnlyEntry(
    StableEntryId EntryId,
    LibraryId LibraryId,
    RelativeAssetPath RelativePath,
    AssetEntryKind Kind,
    long? ContentLength,
    DateTimeOffset LastWriteTimeUtc);

public enum SearchHitReason
{
    Name = 0,
    Path = 1,
}

public sealed record AuthorizedSearchHit(
    AuthorizedLibrary Library,
    ReadOnlyEntry Entry,
    SearchHitReason Reason);

public sealed record ReadPage<T>(IReadOnlyList<T> Items, ReadPageCursor? NextCursor);

public sealed record AuthorizedEntryPage(
    AuthorizedLibrary Library,
    BrowseParentPath ParentPath,
    ReadPage<ReadOnlyEntry> Page);

public sealed record ListLibrariesQuery(
    AuthenticatedSubject Subject,
    ReadPageOptions Page);

public sealed record BrowseEntriesQuery(
    AuthenticatedSubject Subject,
    LibraryId LibraryId,
    BrowseParentPath ParentPath,
    ReadPageOptions Page);

public sealed record SearchAssetsQuery(
    AuthenticatedSubject Subject,
    AssetSearchText SearchText,
    ReadPageOptions Page);
