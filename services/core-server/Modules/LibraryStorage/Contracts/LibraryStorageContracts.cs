namespace AssetLibrary.Modules.LibraryStorage.Contracts;

public readonly record struct LibraryId
{
    public LibraryId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    public Guid Value { get; }

    public static LibraryId New() => new(Guid.NewGuid());
}

public readonly record struct StorageSourceId
{
    public StorageSourceId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    public Guid Value { get; }

    public static StorageSourceId New() => new(Guid.NewGuid());
}

public enum RootPathComparison
{
    CaseSensitive = 0,
    CaseInsensitive = 1,
}

public readonly record struct CanonicalLibraryRoot
{
    public CanonicalLibraryRoot(string value, RootPathComparison comparison)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Replace('\\', '/');
        Validate(normalized);

        Value = TrimEndingSeparator(normalized);
        Comparison = comparison;
    }

    public string Value { get; }

    public RootPathComparison Comparison { get; }

    private static void Validate(string value)
    {
        if (!IsRooted(value) || value.Contains('\0'))
        {
            throw new ArgumentException("A canonical library root must be an absolute path.", nameof(value));
        }

        if (value is "/" || (value.Length == 3 && value[1] == ':' && value[2] == '/'))
        {
            return;
        }

        var trimmed = value.TrimEnd('/');
        var isUnc = value.StartsWith("//", StringComparison.Ordinal);
        var isWindowsDrive = value.Length >= 3 && value[1] == ':' && value[2] == '/';
        var segmentStart = isUnc ? 2 : isWindowsDrive ? 3 : 1;
        if (trimmed.Length < segmentStart)
        {
            throw new ArgumentException("A canonical library root cannot contain only separators.", nameof(value));
        }

        var segments = trimmed[segmentStart..].Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("A canonical library root cannot contain empty or traversal segments.", nameof(value));
        }

        if (isUnc && segments.Length < 2)
        {
            throw new ArgumentException("A canonical UNC root requires a server and share.", nameof(value));
        }
    }

    private static bool IsRooted(string value) =>
        value.StartsWith('/')
        || (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '/');

    private static string TrimEndingSeparator(string value)
    {
        if (value is "/" || (value.Length == 3 && value[1] == ':' && value[2] == '/'))
        {
            return value;
        }

        return value.TrimEnd('/');
    }
}

public enum StorageAvailability
{
    Online = 0,
    Offline = 1,
}

public enum LibraryAccessLevel
{
    ReadOnly = 1,
    ReadWrite = 2,
    Organize = 3,
    LibraryAdministrator = 4,
}

public sealed record LibraryScanTarget(
    LibraryId LibraryId,
    StorageSourceId StorageSourceId,
    CanonicalLibraryRoot Root,
    StorageAvailability Availability);

public sealed record RegisteredLibraryRoot(
    LibraryId LibraryId,
    StorageSourceId StorageSourceId,
    CanonicalLibraryRoot Root);

public interface ILibraryScanTargetQuery
{
    ValueTask<LibraryScanTarget?> FindAsync(LibraryId libraryId, CancellationToken cancellationToken);
}

public interface IRegisteredLibraryRootQuery
{
    IAsyncEnumerable<RegisteredLibraryRoot> ListAsync(
        StorageSourceId storageSourceId,
        CancellationToken cancellationToken);
}
