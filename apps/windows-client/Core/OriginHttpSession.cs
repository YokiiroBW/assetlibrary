using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace AssetLibrary.Windows.Client;

internal sealed class OriginHttpSession : IDisposable
{
    private readonly HttpClient client;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim admission = new(2, 2);
    private readonly ThumbnailHttpReader thumbnails;
    public OriginHttpSession(ServerProfile profile, HttpMessageHandler handler, TimeSpan timeout)
    {
        this.timeout = timeout;
        client = new HttpClient(handler) { BaseAddress = profile.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Add("Origin", profile.Origin);
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoStore = true };
        thumbnails = new ThumbnailHttpReader(client, admission);
    }

    internal static HttpClientHandler CreateHandler(ServerProfile profile)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
        if (profile.CertificateSha256 is not null)
        {
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                profile.AcceptCertificate(certificate, errors, DateTimeOffset.UtcNow);
        }

        return handler;
    }

    public async Task<string> SendAsync(string path, HttpMethod method, string? body, string csrfToken, CancellationToken token,
        string? requestId = null, bool expectNoContent = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-AssetLibrary-CSRF", csrfToken);
        if (requestId is not null) { request.Headers.Add("X-Request-Id", requestId); }
        if (body is not null) { request.Content = new StringContent(body, Encoding.UTF8, "application/json"); }
        var admitted = false;
        try
        {
            await admission.WaitAsync(deadline.Token).ConfigureAwait(false);
            admitted = true;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            // Rejection is authoritative even when an untrusted error body stalls or has no matching request id.
            if (!response.IsSuccessStatusCode) { throw Failure((int)response.StatusCode); }
            if (expectNoContent && response.StatusCode != HttpStatusCode.NoContent) { throw Failure(502); }
            if (response.Content.Headers.ContentLength > ClientTransport.MaximumResponseBytes) { throw Failure(502); }
            await response.Content.LoadIntoBufferAsync(ClientTransport.MaximumResponseBytes, deadline.Token).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new ClientException(504, "timeout", "读取超时，已停止等待。可以重试。"); }
        catch (HttpRequestException)
        { throw new ClientException(503, "connection_failed", "连接失败。请检查网络、服务器地址及证书信任。"); }
        finally { if (admitted) { admission.Release(); } }
    }

    internal Task<byte[]> ThumbnailAsync(Guid library, Guid entry, CancellationToken token) => thumbnails.ReadAsync(library, entry, token);

    private static ClientException Failure(int status) => new(status, status switch
    {
        401 => "unauthenticated",
        403 => "forbidden",
        404 => "not_found",
        429 => "rate_limited",
        410 => "cursor_expired",
        _ => "request_failed",
    }, status switch
    {
        401 => "账号密码无效或登录已过期，请重新登录。",
        403 => "访问被拒绝，已清除当前数据。",
        404 => "条目不存在或已不可访问，已清除当前数据。",
        429 => "请求过于频繁，请稍后重试。",
        410 => "分页已过期，请刷新后继续。",
        >= 300 and < 400 => "服务器返回了重定向。请使用其直接 HTTPS 地址重新连接。",
        _ => "服务器暂时不可用或响应无效，请重试。",
    });

    public void Dispose() { client.Dispose(); thumbnails.Dispose(); admission.Dispose(); }
}
