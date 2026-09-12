using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

internal static class ThumbnailTestSupport
{
    internal static Guid LibraryId => new("b0662020-534c-4c55-9a29-a94be4cc596a");
    internal static Guid EntryId => new("ce6c38bc-503f-407b-826d-5d09912256f6");
    internal const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFklEQVR4nGP4z8DwHwgbGIC0A5BgAAA3IQS9WUyapgAAAABJRU5ErkJggg==";
    internal static byte[] Png => Convert.FromBase64String(PngBase64);
    internal static byte[] ExpectedPixels => [0, 0, 255, 255, 0, 128, 0, 128, 64, 0, 0, 64, 0, 0, 0, 0];
    internal static string Executable => Path.ChangeExtension(typeof(SnapshotStore).Assembly.Location, ".exe");
    internal static string Repository
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            { if (File.Exists(Path.Combine(directory.FullName, "global.json"))) { return directory.FullName; } }
            throw new InvalidOperationException("Repository fixture unavailable.");
        }
    }
    internal static HttpResponseMessage ImageResponse()
    {
        var message = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return message;
    }
    internal static JsonObject LibraryJson
    {
        get { var value = ProtocolFixture.LibraryJson; value["library_id"] = LibraryId.ToString(); return value; }
    }
    internal static async Task<HttpResponseMessage> PageAsync(HttpRequestMessage request)
    {
        var entry = ProtocolFixture.EntryJson(); entry["library_id"] = LibraryId.ToString(); entry["entry_id"] = EntryId.ToString();
        var input = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
        return await ProtocolFixture.ResultAsync(request, input["operation"]!.GetValue<string>() == "libraries.list"
            ? new JsonObject { ["items"] = new JsonArray(LibraryJson), ["next_cursor"] = null }
            : new JsonObject { ["library"] = LibraryJson, ["parent_relative_path"] = "", ["items"] = new JsonArray(entry), ["next_cursor"] = null });
    }
    internal static async Task<ThumbnailRequest> FileAsync(SnapshotStore store)
    {
        var libraries = await HostTestSupport.SettledAsync(store);
        var files = await HostTestSupport.SettledAsync(store, HostTestSupport.Open(libraries, libraries.Items[0]));
        return new ThumbnailRequest(17, files.Epoch, files.Items[0].Node);
    }
    internal static ClientTransport Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) =>
        new(ProtocolFixture.Profile, new ProtocolFixture(response), TimeSpan.FromSeconds(3));
}
