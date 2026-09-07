using System.Text.Json.Nodes;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using static AssetLibrary.CoreServer.Adapters.AssetLink.AssetLinkReadFields;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

internal static class AssetLinkReadBodyParser
{
    public static AssetLinkReadRequest Parse(string requestId, string operation, AuthenticatedSubject subject, JsonObject body, ReadPageOptions page)
    {
        return operation switch
        {
            "libraries.list" => new ListLibrariesAssetLinkRequest(
                requestId,
                new ListLibrariesQuery(subject, page, Category(body))),
            "libraries.get" => new GetLibraryAssetLinkRequest(requestId,
                new GetLibraryQuery(subject, LibraryId(body), DetailTimeout(body, page))),
            "entries.get" => new GetEntryAssetLinkRequest(requestId,
                new GetEntryQuery(subject, LibraryId(body), new StableEntryId(Identifier(body, "entry_id")),
                    DetailTimeout(body, page))),
            "entries.browse" => new BrowseEntriesAssetLinkRequest(
                requestId,
                new BrowseEntriesQuery(
                    subject,
                    LibraryId(body),
                    new BrowseParentPath(OptionalString(body, "parent_relative_path")),
                    page,
                    new EntryBrowseOptions(Option<EntrySortBy>(body, "sort_by"),
                        Option<ReadSortDirection>(body, "sort_direction"), Option<EntryKindFilter>(body, "kind"),
                        OptionString(body, "name_filter")),
                    body.ContainsKey("anchor_entry_id") ? new StableEntryId(Identifier(body, "anchor_entry_id")) : null)),
            "assets.search" => new SearchAssetsAssetLinkRequest(
                requestId,
                new SearchAssetsQuery(
                    subject,
                    new AssetSearchText(RequiredString(body, "query")),
                    page, SearchScope(body))),
            _ => new UnsupportedAssetLinkReadRequest(requestId),
        };
    }

    private static LibraryCategory? Category(JsonObject body) =>
        body.ContainsKey("category") ? LibraryCategories.Parse(RequiredString(body, "category")) : null;

    private static AssetSearchScopeOptions SearchScope(JsonObject body) =>
        new(Option<AssetSearchScope>(body, "scope"),
            body.ContainsKey("library_id") ? LibraryId(body) : null,
            body.ContainsKey("parent_relative_path") ? new BrowseParentPath(RequiredString(body, "parent_relative_path")) : null);

    private static TimeSpan DetailTimeout(JsonObject body, ReadPageOptions page)
    {
        if (body.ContainsKey("cursor") || body.ContainsKey("page_size"))
        {
            throw new ArgumentException("A detail request cannot carry pagination fields.");
        }

        return page.Timeout;
    }

    private static T Option<T>(JsonObject body, string name) where T : struct, Enum
    {
        var value = OptionString(body, name);
        if (value is null)
        {
            return default;
        }

        return Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed)
            && value.All(character => character is >= 'a' and <= 'z') ? parsed : throw new ArgumentException("A query option is invalid.");
    }

    private static string? OptionString(JsonObject body, string name) =>
        body.ContainsKey(name) ? RequiredString(body, name) : null;
    private static LibraryId LibraryId(JsonObject body)
    {
        return new LibraryId(Identifier(body, "library_id"));
    }

    private static Guid Identifier(JsonObject body, string name)
    {
        var value = RequiredString(body, name);
        if (!Guid.TryParseExact(value, "D", out var parsed))
        {
            throw new FormatException("A canonical library ID is required.");
        }

        return parsed;
    }

}
