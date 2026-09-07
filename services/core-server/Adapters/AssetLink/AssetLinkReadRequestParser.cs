using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.AssetLink;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

internal static class AssetLinkReadRequestParser
{
    private const int MaximumRequestIdLength = 128;

    public static AssetLinkReadRequest Parse(ClaimsPrincipal principal, string payload)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(payload);
        var subject = Subject(principal);
        var message = AssetLinkCodec.Parse(payload);
        if (message is not ControlRequestMessage request)
        {
            throw new JsonException("A control request is required.");
        }

        var requestId = RequestId(request.RequestId);
        if (request.IdempotencyKey is not null || request.CancelOf is not null)
        {
            throw new ArgumentException("Read-only controls cannot carry write or cancellation identity.");
        }

        var page = Page(request.Body, request.TimeoutMs);
        return request.Operation switch
        {
            "libraries.list" => new ListLibrariesAssetLinkRequest(
                requestId,
                new ListLibrariesQuery(subject, page, Category(request.Body))),
            "libraries.get" => new GetLibraryAssetLinkRequest(requestId,
                new GetLibraryQuery(subject, LibraryId(request.Body), DetailTimeout(request.Body, page))),
            "entries.get" => new GetEntryAssetLinkRequest(requestId,
                new GetEntryQuery(subject, LibraryId(request.Body), new StableEntryId(Identifier(request.Body, "entry_id")),
                    DetailTimeout(request.Body, page))),
            "entries.browse" => new BrowseEntriesAssetLinkRequest(
                requestId,
                new BrowseEntriesQuery(
                    subject,
                    LibraryId(request.Body),
                    new BrowseParentPath(OptionalString(request.Body, "parent_relative_path")),
                    page,
                    new EntryBrowseOptions(Option<EntrySortBy>(request.Body, "sort_by"),
                        Option<ReadSortDirection>(request.Body, "sort_direction"), Option<EntryKindFilter>(request.Body, "kind"),
                        OptionString(request.Body, "name_filter")),
                    request.Body.ContainsKey("anchor_entry_id") ? new StableEntryId(Identifier(request.Body, "anchor_entry_id")) : null)),
            "assets.search" => new SearchAssetsAssetLinkRequest(
                requestId,
                new SearchAssetsQuery(
                    subject,
                    new AssetSearchText(RequiredString(request.Body, "query")),
                    page, SearchScope(request.Body))),
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
            && parsed.ToString().ToLowerInvariant() == value ? parsed : throw new ArgumentException("A query option is invalid.");
    }

    private static string? OptionString(JsonObject body, string name) =>
        body.ContainsKey(name) ? RequiredString(body, name) : null;

    private static AuthenticatedSubject Subject(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new AssetLinkAuthenticationException();
        }

        var candidates = principal.Claims
            .Where(claim => claim.Type is ClaimTypes.NameIdentifier or "sub")
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new AssetLinkAuthenticationException();
        }

        try
        {
            return new AuthenticatedSubject(candidates[0]);
        }
        catch (ArgumentException)
        {
            throw new AssetLinkAuthenticationException();
        }
    }

    private static ReadPageOptions Page(JsonObject body, int? timeoutMilliseconds)
    {
        var pageSize = OptionalInteger(body, "page_size") ?? ReadPageOptions.DefaultPageSize;
        var cursorValue = OptionalString(body, "cursor");
        ReadPageCursor? cursor = cursorValue is null ? null : new ReadPageCursor(cursorValue);
        var timeout = timeoutMilliseconds is null
            ? ReadPageOptions.DefaultTimeout
            : TimeSpan.FromMilliseconds(timeoutMilliseconds.Value);
        return new ReadPageOptions(pageSize, timeout, cursor);
    }

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

    private static string RequestId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumRequestIdLength
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("The request ID is invalid.", nameof(value));
        }

        return value;
    }

    private static int? OptionalInteger(JsonObject body, string propertyName)
    {
        var value = body[propertyName];
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<int>();
        }
        catch (InvalidOperationException error)
        {
            throw new JsonException($"The property {propertyName} has an invalid type.", error);
        }
    }

    private static string RequiredString(JsonObject body, string propertyName)
    {
        var value = body[propertyName]
            ?? throw new JsonException($"The required property {propertyName} is missing.");
        return StringValue(value, propertyName);
    }

    private static string? OptionalString(JsonObject body, string propertyName)
    {
        var value = body[propertyName];
        return value is null ? null : StringValue(value, propertyName);
    }

    private static string StringValue(JsonNode value, string propertyName)
    {
        try
        {
            return value.GetValue<string>();
        }
        catch (InvalidOperationException error)
        {
            throw new JsonException($"The property {propertyName} has an invalid type.", error);
        }
    }
}
