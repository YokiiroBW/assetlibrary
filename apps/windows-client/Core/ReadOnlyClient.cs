using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssetLibrary.Windows.Client;

public sealed class ReadOnlyClient(ClientTransport transport)
{
    public const int PageSize = 100;

    public Task<ResultPage<LibraryItem>> LibrariesAsync(string? cursor, string? category, CancellationToken token)
    {
        var body = PageBody(cursor);
        if (category is not null) { body["category"] = category; }
        return DecodeAsync("libraries.list", body, value => ResponseReader.Page(value, ResponseReader.Library), token);
    }

    public Task<ResultPage<EntryItem>> EntriesAsync(WorkspaceLocation location, string? cursor, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        return location.IsSearch ? SearchAsync(location, cursor, token) : BrowseAsync(location, cursor, token);
    }

    private Task<ResultPage<EntryItem>> BrowseAsync(WorkspaceLocation location, string? cursor, CancellationToken token)
    {
        var library = location.Library ?? throw new ArgumentException("Library required.", nameof(location));
        var options = location.Options ?? new BrowseQuery();
        var body = PageBody(cursor);
        body["library_id"] = library.Id;
        body["parent_relative_path"] = location.ParentPath;
        body["sort_by"] = options.SortBy;
        body["sort_direction"] = options.SortDirection;
        body["kind"] = options.Kind;
        body["name_filter"] = options.NameFilter;
        if (cursor is null && location.AnchorId is not null) { body["anchor_entry_id"] = location.AnchorId; }
        return DecodeAsync("entries.browse", body, value =>
        {
            var actualLibrary = ResponseReader.Library(ResponseReader.Object(value["library"]));
            if (actualLibrary.Id != library.Id || ResponseReader.Text(value, "parent_relative_path") != location.ParentPath)
            { throw new JsonException("Directory scope mismatch."); }
            return ResponseReader.Page(value, entry =>
            {
                var item = ResponseReader.Entry(entry);
                if (item.LibraryId != library.Id) { throw new JsonException("Library scope mismatch."); }
                return new EntryItem(actualLibrary, item);
            });
        }, token);
    }

    private Task<ResultPage<EntryItem>> SearchAsync(WorkspaceLocation location, string? cursor, CancellationToken token)
    {
        var body = PageBody(cursor);
        body["query"] = location.Query;
        body["scope"] = location.SearchScope;
        if (location.SearchScope != "all") { body["library_id"] = location.Library?.Id; }
        if (location.SearchScope == "directory") { body["parent_relative_path"] = location.ParentPath; }
        return DecodeAsync("assets.search", body, value => ResponseReader.Page(value, hit =>
        {
            var result = ResponseReader.Detail(hit);
            if (location.SearchScope != "all" && result.Library.Id != location.Library?.Id)
            { throw new JsonException("Search library scope mismatch."); }
            if (location.SearchScope == "directory" && location.ParentPath.Length > 0
                && !result.Entry.RelativePath.StartsWith(location.ParentPath + "/", StringComparison.Ordinal))
            { throw new JsonException("Search directory scope mismatch."); }
            return result;
        }), token);
    }

    public Task<EntryItem> DetailAsync(EntryItem item, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(item);
        return DecodeAsync("entries.get", new JsonObject { ["library_id"] = item.Library.Id, ["entry_id"] = item.Entry.Id }, value =>
        {
            var result = ResponseReader.Detail(value);
            if (result.Library.Id != item.Library.Id || result.Entry.Id != item.Entry.Id)
            { throw new JsonException("Detail scope mismatch."); }
            return result;
        }, token);
    }

    private async Task<T> DecodeAsync<T>(string operation, JsonObject body, Func<JsonObject, T> decode, CancellationToken token)
    {
        var result = await transport.RequestAsync(operation, body, token).ConfigureAwait(false);
        try { return decode(result); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw new ClientException(502, "invalid_response", "服务响应的条目、范围或数据格式无效。"); }
    }

    private static JsonObject PageBody(string? cursor)
    {
        var body = new JsonObject { ["page_size"] = PageSize };
        if (cursor is not null) { body["cursor"] = cursor; }
        return body;
    }
}
