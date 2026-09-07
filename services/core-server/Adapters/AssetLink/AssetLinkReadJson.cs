using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

internal static class AssetLinkReadJson
{
    public static AssetLinkProtocolResponse Success(string requestId, JsonObject body) =>
        Response(
            200,
            new JsonObject
            {
                ["message_type"] = "control.result",
                ["request_id"] = requestId,
                ["ok"] = true,
                ["body"] = body,
            });

    public static AssetLinkProtocolResponse Error(
        int statusCode,
        string requestId,
        string code,
        string message) =>
        Response(
            statusCode,
            new JsonObject
            {
                ["message_type"] = "error",
                ["request_id"] = requestId,
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message,
                },
            });

    public static JsonObject Libraries(ReadPage<AuthorizedLibrary> result) =>
        new()
        {
            ["items"] = Array(result.Items.Select(Library)),
            ["next_cursor"] = result.NextCursor?.Value,
        };

    public static JsonObject Entries(AuthorizedEntryPage result)
    {
        var body = new JsonObject
        {
            ["library"] = Library(result.Library),
            ["parent_relative_path"] = result.ParentPath.Value,
            ["items"] = Array(result.Page.Items.Select(Entry)),
            ["next_cursor"] = result.Page.NextCursor?.Value,
        };
        if (result.AnchorEntryId is { } anchor)
        {
            body["anchor_entry_id"] = anchor.Value.ToString("D");
        }

        return body;
    }

    public static JsonObject Search(ReadPage<AuthorizedSearchHit> result) =>
        new()
        {
            ["items"] = Array(result.Items.Select(Hit)),
            ["next_cursor"] = result.NextCursor?.Value,
        };

    private static AssetLinkProtocolResponse Response(int statusCode, JsonObject body) =>
        new(statusCode, body.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));

    public static JsonObject LibraryDetail(AuthorizedLibrary library) => new() { ["library"] = Library(library) };

    public static JsonObject EntryDetail(AuthorizedEntryDetail result) =>
        new() { ["library"] = Library(result.Library), ["entry"] = Entry(result.Entry) };

    private static JsonObject Library(AuthorizedLibrary library) =>
        new()
        {
            ["library_id"] = library.LibraryId.Value.ToString("D"),
            ["display_name"] = library.DisplayName,
            ["availability"] = Availability(library.Availability),
            ["access_level"] = AccessLevel(library.AccessLevel),
            ["category"] = LibraryCategories.ToWire(library.Category),
        };

    private static JsonObject Entry(ReadOnlyEntry entry) =>
        new()
        {
            ["entry_id"] = entry.EntryId.Value.ToString("D"),
            ["library_id"] = entry.LibraryId.Value.ToString("D"),
            ["relative_path"] = entry.RelativePath.Value,
            ["name"] = entry.RelativePath.Name,
            ["kind"] = EntryKind(entry.Kind),
            ["content_length"] = entry.ContentLength?.ToString(CultureInfo.InvariantCulture),
            ["last_write_time_utc"] = entry.LastWriteTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        };

    private static JsonObject Hit(AuthorizedSearchHit hit) =>
        new()
        {
            ["library"] = Library(hit.Library),
            ["entry"] = Entry(hit.Entry),
            ["hit_reason"] = hit.Reason is SearchHitReason.Name ? "name" : "path",
        };

    private static JsonArray Array(IEnumerable<JsonObject> items)
    {
        var result = new JsonArray();
        foreach (var item in items)
        {
            result.Add(item);
        }

        return result;
    }

    private static string Availability(StorageAvailability availability) => availability switch
    {
        StorageAvailability.Online => "online",
        StorageAvailability.Offline => "offline",
        _ => throw new ArgumentOutOfRangeException(nameof(availability)),
    };

    private static string AccessLevel(LibraryAccessLevel accessLevel) => accessLevel switch
    {
        LibraryAccessLevel.ReadOnly => "read_only",
        LibraryAccessLevel.ReadWrite => "read_write",
        LibraryAccessLevel.Organize => "organize",
        LibraryAccessLevel.LibraryAdministrator => "library_administrator",
        _ => throw new ArgumentOutOfRangeException(nameof(accessLevel)),
    };

    private static string EntryKind(AssetEntryKind kind) => kind switch
    {
        AssetEntryKind.File => "file",
        AssetEntryKind.Directory => "directory",
        AssetEntryKind.ReparseFile => "reparse_file",
        AssetEntryKind.ReparseDirectory => "reparse_directory",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

public sealed record AssetLinkProtocolResponse(int StatusCode, string Json);
