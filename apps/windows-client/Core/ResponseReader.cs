using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.AssetLink;

namespace AssetLibrary.Windows.Client;

internal static class ResponseReader
{
    private static readonly string[] Categories = ["photos", "images", "videos", "music", "projects", "documents", "characters", "general"];
    public static string Text(JsonObject value, string key, int maximum = 4096) =>
        value[key] is JsonValue field && field.TryGetValue<string>(out var text) && text.Length <= maximum
            ? text : throw new JsonException("Invalid text field.");

    public static JsonObject Object(JsonNode? value) => value as JsonObject ?? throw new JsonException("Object required.");
    public static string? OptionalText(JsonObject value, string key) => value[key] is null ? null : Text(value, key);
    public static DateTimeOffset Timestamp(JsonObject value, string key) =>
        DateTimeOffset.TryParse(Text(value, key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? time : throw new JsonException("Invalid timestamp.");

    public static ResultPage<T> Page<T>(JsonObject value, Func<JsonObject, T> decode)
    {
        if (value["items"] is not JsonArray items || items.Count > ReadOnlyClient.PageSize)
        {
            throw new JsonException("Invalid page bound.");
        }

        var cursor = OptionalText(value, "next_cursor");
        if (cursor is { Length: 0 }) { throw new JsonException("Empty cursor."); }
        return new ResultPage<T>(items.Select(item => decode(Object(item))).ToArray(), cursor);
    }

    public static LibraryItem Library(JsonObject value)
    {
        var availability = Text(value, "availability");
        var access = Text(value, "access_level");
        var category = OptionalText(value, "category") ?? "general";
        if (availability is not ("online" or "offline")
            || access is not ("read_only" or "read_write" or "organize" or "library_administrator")
            || !Categories.Contains(category, StringComparer.Ordinal))
        {
            throw new JsonException("Unknown library state.");
        }

        return new LibraryItem(Text(value, "library_id"), Text(value, "display_name"), availability, access, category);
    }

    public static AssetEntry Entry(JsonObject value)
    {
        var kind = Text(value, "kind");
        if (kind is not ("file" or "directory" or "reparse_file" or "reparse_directory"))
        {
            throw new JsonException("Unknown entry state.");
        }

        var length = OptionalText(value, "content_length");
        if (length is not null) { _ = AssetLinkUInt64.Parse(length); }
        return new AssetEntry(Text(value, "entry_id"), Text(value, "library_id"), Text(value, "relative_path"),
            Text(value, "name"), kind, length, Timestamp(value, "last_write_time_utc"));
    }

    public static EntryItem Detail(JsonObject value)
    {
        var library = Library(Object(value["library"]));
        var entry = Entry(Object(value["entry"]));
        if (library.Id != entry.LibraryId) { throw new JsonException("Mismatched library."); }
        return new EntryItem(library, entry);
    }

    public static ClientSession Session(JsonObject value)
    {
        if (value["authenticated"]?.GetValue<bool>() != true) { throw new JsonException("Session required."); }
        var csrf = Text(value, "csrf_token");
        if (csrf.Length != 43 || csrf.Any(char.IsControl)) { throw new JsonException("Invalid session."); }
        return new ClientSession(Text(value, "principal_id"), Text(value, "display_name"), csrf, Timestamp(value, "absolute_expires_at"));
    }
}
