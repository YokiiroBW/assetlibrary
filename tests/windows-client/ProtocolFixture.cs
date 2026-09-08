using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

internal sealed class ProtocolFixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => response(request, token);
    internal static ServerProfile Profile => new("https://fixture.example:5443");
    internal static LibraryItem Library => new("library-a", "样例库", "online", "read_only", "general");
    internal static JsonObject LibraryJson => new()
    {
        ["library_id"] = "library-a",
        ["display_name"] = "样例库",
        ["availability"] = "online",
        ["access_level"] = "read_only",
        ["category"] = "general",
    };
    internal static JsonObject EntryJson(string id = "entry-a", string path = "fixture.txt") => new()
    {
        ["entry_id"] = id,
        ["library_id"] = "library-a",
        ["relative_path"] = path,
        ["name"] = path.Split('/')[^1],
        ["kind"] = "file",
        ["content_length"] = "42",
        ["last_write_time_utc"] = "2026-09-01T10:00:00Z",
    };
    internal static JsonObject Page(string name = "fixture.txt", string? cursor = null) => new()
    {
        ["library"] = LibraryJson,
        ["parent_relative_path"] = "",
        ["items"] = new JsonArray(EntryJson(path: name)),
        ["next_cursor"] = cursor,
    };
    internal static HttpResponseMessage Json(JsonObject value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };
    internal static async Task<HttpResponseMessage> ResultAsync(HttpRequestMessage request, JsonObject body)
    {
        var json = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
        return Json(new JsonObject { ["message_type"] = "control.result", ["request_id"] = json["request_id"]!.GetValue<string>(), ["ok"] = true, ["body"] = body.DeepClone() });
    }
}
