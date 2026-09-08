using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AssetLibrary.Preview.Tests;

internal sealed class LivePreviewConnection : IDisposable
{
    private readonly JsonDocument settings;
    private readonly HttpClient client;
    private string? csrf;
    public Guid LibraryId => settings.RootElement.GetProperty("library_id").GetGuid();

    private LivePreviewConnection(JsonDocument settings)
    {
        this.settings = settings;
        var origin = new Uri(settings.RootElement.GetProperty("origin").GetString()!);
        Assert.AreEqual("https", origin.Scheme);
        Assert.AreEqual("localhost", origin.Host);
        var fingerprint = settings.RootElement.GetProperty("certificate_sha256").GetString()!;
        client = LivePreviewTls.Create(origin, fingerprint);
        client.DefaultRequestHeaders.Add("Origin", origin.GetLeftPart(UriPartial.Authority));
    }

    public static async Task<LivePreviewConnection> OpenAsync()
    {
        var path = Environment.GetEnvironmentVariable("ASSETLIBRARY_PREVIEW_CONNECTION_FILE");
        if (string.IsNullOrWhiteSpace(path)) Assert.Inconclusive("The existing real Core/PostgreSQL fixture must supply its private connection file.");
        await using var file = File.OpenRead(path!);
        var bytes = new byte[16 * 1024 + 1];
        var count = await file.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false);
        Assert.IsLessThanOrEqualTo(16 * 1024, count);
        return new LivePreviewConnection(JsonDocument.Parse(bytes.AsMemory(0, count)));
    }

    public async Task SignInAsync(bool invisible = false)
    {
        var root = settings.RootElement;
        using var response = await client.PostAsJsonAsync("/assetlink/v1/auth/login", new
        {
            account_name = root.GetProperty(invisible ? "invisible_account_name" : "account_name").GetString(),
            password = root.GetProperty(invisible ? "invisible_account_password" : "password").GetString(),
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        csrf = json.RootElement.GetProperty("csrf_token").GetString();
    }

    public async Task<Dictionary<string, Guid>> ImageEntriesAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/assetlink/v1/control");
        request.Headers.Add("X-AssetLibrary-CSRF", csrf);
        var id = Guid.NewGuid();
        request.Content = JsonContent.Create(new
        {
            message_type = "control.request",
            request_id = id,
            operation = "entries.browse",
            body = new { library_id = LibraryId, parent_relative_path = "图片样例", page_size = 100 },
        });
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(id, json.RootElement.GetProperty("request_id").GetGuid());
        return json.RootElement.GetProperty("body").GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!, item => item.GetProperty("entry_id").GetGuid(), StringComparer.Ordinal);
    }

    public string ImagePath(Guid entry, string query = "variant=thumbnail") =>
        $"/assetlink/v1/libraries/{LibraryId:D}/entries/{entry:D}/image?{query}";

    public async Task<HttpResponseMessage> SendAsync(string path, HttpMethod? method = null, string? origin = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    public static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString()!;
    }

    public void Dispose()
    {
        client.Dispose();
        settings.Dispose();
        GC.SuppressFinalize(this);
    }
}
