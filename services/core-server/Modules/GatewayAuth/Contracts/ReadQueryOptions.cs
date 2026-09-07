using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Contracts;

public enum EntrySortBy { Name, Modified, Size }
public enum ReadSortDirection { Asc, Desc }
public enum EntryKindFilter { All, Files, Directories }
public enum AssetSearchScope { All, Library, Directory }

public readonly record struct EntryBrowseOptions
{
    private readonly string? nameFilter;

    public EntryBrowseOptions(EntrySortBy sortBy, ReadSortDirection direction, EntryKindFilter kind, string? nameFilter = null)
    {
        if (!Enum.IsDefined(sortBy) || !Enum.IsDefined(direction) || !Enum.IsDefined(kind))
        {
            throw new ArgumentException("The browse options are invalid.");
        }

        var normalized = (nameFilter ?? string.Empty).Trim();
        if (normalized.Length > 200 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The name filter is invalid.", nameof(nameFilter));
        }

        SortBy = sortBy;
        Direction = direction;
        Kind = kind;
        this.nameFilter = normalized;
    }

    public EntrySortBy SortBy { get; }
    public ReadSortDirection Direction { get; }
    public EntryKindFilter Kind { get; }
    public string NameFilter => nameFilter ?? string.Empty;

    public bool Includes(AssetEntryKind kind) => Kind switch
    {
        EntryKindFilter.All => true,
        EntryKindFilter.Files => kind is AssetEntryKind.File or AssetEntryKind.ReparseFile,
        EntryKindFilter.Directories => kind is AssetEntryKind.Directory or AssetEntryKind.ReparseDirectory,
        _ => false,
    };
}

public readonly record struct AssetSearchScopeOptions
{
    public AssetSearchScopeOptions(AssetSearchScope scope, LibraryId? libraryId = null, BrowseParentPath? parentPath = null)
    {
        if (!Enum.IsDefined(scope)
            || (scope == AssetSearchScope.All) != (libraryId is null)
            || (scope == AssetSearchScope.Directory) != (parentPath is not null)
            || libraryId?.Value == Guid.Empty)
        {
            throw new ArgumentException("The search scope is invalid.");
        }

        Scope = scope;
        LibraryId = libraryId;
        ParentPath = parentPath;
    }

    public AssetSearchScope Scope { get; }
    public LibraryId? LibraryId { get; }
    public BrowseParentPath? ParentPath { get; }

    public bool Includes(ReadOnlyEntry entry) =>
        (LibraryId is null || entry.LibraryId == LibraryId)
        && (ParentPath is not { } parent || parent.Value.Length == 0
            || entry.RelativePath.Value.StartsWith(parent.Value + '/', StringComparison.Ordinal));
}

public sealed class AuthorizedReadNotFoundException : InvalidOperationException
{
    public AuthorizedReadNotFoundException() : base("The requested resource is not available.") { }
}
