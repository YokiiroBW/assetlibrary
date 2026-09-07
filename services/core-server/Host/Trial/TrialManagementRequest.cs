using System.Text.Json.Nodes;
using AssetLibrary.AssetLink;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialManagementRequest
{
    public static ManagementOperation Operation(ControlRequestMessage request, Guid principalId) =>
        Guid.TryParseExact(request.IdempotencyKey, "D", out var key) && key != Guid.Empty
            ? new ManagementOperation(principalId, key) : throw new ArgumentException("A valid operation identity is required.");

    public static string Text(JsonObject body, string name) =>
        body[name] is JsonValue value && value.TryGetValue<string>(out var text) && text is not null
            ? text : throw new ArgumentException("A required field is missing.");

    public static Guid Identifier(JsonObject body, string name) =>
        Guid.TryParseExact(Text(body, name), "D", out var value) && value != Guid.Empty
            ? value : throw new ArgumentException("A valid identifier is required.");

}
