using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.AssetLink;

namespace AssetLibrary.Windows.Client;

public sealed class ClientTransport : IDisposable
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    public const int MaximumResponseBytes = 1_048_576;
    private readonly OriginHttpSession client;

    private string csrfToken = "";

    public ClientTransport(ServerProfile profile) : this(profile, OriginHttpSession.CreateHandler(profile), RequestTimeout) { }

    public ClientTransport(ServerProfile profile, HttpMessageHandler handler, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(handler);
        client = new OriginHttpSession(profile, handler, timeout);
    }
    public async Task<ClientSession> SignInAsync(string account, string password, CancellationToken token)
    {
        var body = new JsonObject { ["account_name"] = account, ["password"] = password };
        var result = await client.SendAsync("assetlink/v1/auth/login", HttpMethod.Post, body.ToJsonString(), csrfToken, token).ConfigureAwait(false);
        var session = DecodeSession(result);
        csrfToken = session.CsrfToken;
        return session;
    }

    public async Task<ClientSession> SessionAsync(CancellationToken token)
    {
        var result = await client.SendAsync("assetlink/v1/auth/session", HttpMethod.Get, null, csrfToken, token).ConfigureAwait(false);
        var session = DecodeSession(result);
        csrfToken = session.CsrfToken;
        return session;
    }

    private static ClientSession DecodeSession(string text)
    {
        try { return ResponseReader.Session(ResponseReader.Object(JsonNode.Parse(text))); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        { throw new ClientException(502, "invalid_response", "服务返回了无法识别的登录状态。"); }
    }

    public async Task SignOutAsync(CancellationToken token)
    {
        _ = await client.SendAsync("assetlink/v1/auth/logout", HttpMethod.Post, "{}", csrfToken, token, expectNoContent: true).ConfigureAwait(false);
        csrfToken = "";
    }

    public async Task<JsonObject> RequestAsync(string operation, JsonObject body, CancellationToken token)
    {
        var requestId = Guid.NewGuid().ToString();
        var request = new ControlRequestMessage(new JsonObject
        {
            ["message_type"] = AssetLinkMessageTypes.ControlRequest,
            ["request_id"] = requestId,
            ["operation"] = operation,
            ["body"] = body,
            ["timeout_ms"] = 5000,
        });
        var text = await client.SendAsync("assetlink/v1/control", HttpMethod.Post, request.ToJson(), csrfToken, token, requestId).ConfigureAwait(false);
        try
        {
            var message = AssetLinkCodec.Parse(text);
            if (ResponseReader.Text(message.Raw, "request_id") != requestId) { throw new JsonException("Response correlation failed."); }
            if (message is not ControlResultMessage result || !result.Ok) { throw new JsonException("Result required."); }
            return result.Body;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        { throw new ClientException(502, "invalid_response", "响应关联或协议格式无效，请重试。"); }
    }

    public void Dispose() { csrfToken = ""; client.Dispose(); }
}
