using System.Security.Claims;
using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Adapters.AssetLink;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.WebGateway.Tests;

internal static class GatewayTestData
{
    public static ReadOnlyAssetLinkProtocol Protocol(FakeAuthorizedReadModelQuery fake) =>
        new(
            Service(fake),
            NullLogger<ReadOnlyAssetLinkProtocol>.Instance);

    public static ReadOnlyBrowseService Service(FakeAuthorizedReadModelQuery fake) =>
        new(fake, NullLogger<ReadOnlyBrowseService>.Instance);

    public static ClaimsPrincipal Principal(string subject) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], "test"));

    public static string Request(
        string operation,
        string body,
        int? timeoutMilliseconds = null) =>
        $"{{\"message_type\":\"control.request\",\"request_id\":\"request-1\","
        + $"\"operation\":\"{operation}\",\"body\":{body}"
        + (timeoutMilliseconds is null ? "}" : $",\"timeout_ms\":{timeoutMilliseconds.Value}}}");

    public static JsonObject Body(AssetLinkProtocolResponse response) =>
        JsonNode.Parse(response.Json)!["body"]!.AsObject();

    public static string ErrorCode(AssetLinkProtocolResponse response) =>
        JsonNode.Parse(response.Json)!["error"]!["code"]!.GetValue<string>();

    public static AuthorizedLibrary Library(LibraryId id) =>
        new(id, "Library", StorageAvailability.Online, LibraryAccessLevel.ReadOnly);

    public static ReadOnlyEntry Entry(LibraryId libraryId, string path) =>
        new(
            StableEntryId.New(),
            libraryId,
            new RelativeAssetPath(path),
            AssetEntryKind.File,
            12,
            DateTimeOffset.Parse("2026-09-04T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}
