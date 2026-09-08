using System.Globalization;
using AssetLibrary.AssetLink;

namespace AssetLibrary.Windows.Client;

public sealed record ClientSession(string PrincipalId, string DisplayName, string CsrfToken, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(ClientSession);
}
public sealed record LibraryItem(string Id, string Name, string Availability, string AccessLevel, string Category)
{
    public string StatusLabel => Availability == "offline" ? "离线 · 已提交索引" : "在线";
    public override string ToString() => Name;
}

public sealed record AssetEntry(string Id, string LibraryId, string RelativePath, string Name, string Kind, string? ContentLength, DateTimeOffset ModifiedAt)
{
    public bool IsDirectory => Kind is "directory" or "reparse_directory";
    public bool IsNavigable => Kind == "directory";
    public string KindLabel => Kind switch
    {
        "directory" => "真实文件夹",
        "reparse_directory" => "链接文件夹（不跟随）",
        "reparse_file" => "链接文件",
        _ => "文件",
    };
    public string Glyph => IsDirectory ? "\uE8B7" : "\uE7C3";
    public string SizeLabel => ContentLength is null ? "—" : FormatSize(AssetLinkUInt64.Parse(ContentLength));
    public string ModifiedLabel => ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string Summary => $"{KindLabel}  ·  {SizeLabel}";
    public string Parent => RelativePath.Contains('/', StringComparison.Ordinal) ? RelativePath[..RelativePath.LastIndexOf('/')] : "";

    private static string FormatSize(ulong bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value.ToString(unit == 0 ? "0" : "0.##", CultureInfo.CurrentCulture)} {units[unit]}";
    }
}

public sealed record EntryItem(LibraryItem Library, AssetEntry Entry)
{
    public string Name => Entry.Name;
    public string Glyph => Entry.Glyph;
    public string Summary => Entry.Summary;
    public string Location => $"{Library.Name} / {Entry.RelativePath}";
    public string Modified => Entry.ModifiedLabel;
    public override string ToString() => Name;
}

public sealed record ResultPage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record BrowseQuery(string SortBy = "name", string SortDirection = "asc", string Kind = "all", string NameFilter = "");
public sealed record WorkspaceLocation(LibraryItem? Library = null, string ParentPath = "", string Query = "", string SearchScope = "all", BrowseQuery? Options = null, string? AnchorId = null)
{
    public bool IsSearch => Query.Length > 0;
    public bool IsHome => Library is null && !IsSearch;
}

public sealed class ClientException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public bool ClearsData => Status is 401 or 403 or 404;
}
