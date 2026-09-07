using System.Security.Cryptography;
using System.Text.Json;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed class TrialBrowserCookieCodec
{
    public const string CookieName = "__Host-AssetLibrary-Session";
    private const int MaximumCookieLength = 2048;
    private readonly IDataProtector protector;
    private readonly TimeProvider clock;

    public TrialBrowserCookieCodec(IDataProtectionProvider protection, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(protection);
        protector = protection.CreateProtector("AssetLibrary.GatewayAuth.BrowserCookie.v1");
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void Write(HttpResponse response, BrowserSessionCredentials credentials)
    {
        var payload = new CookiePayload(
            1, credentials.SessionToken.Export(), credentials.CsrfToken.Export(), credentials.AbsoluteExpiresAt);
        var cleartext = JsonSerializer.SerializeToUtf8Bytes(payload);
        try
        {
            var cookie = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(protector.Protect(cleartext));
            response.Cookies.Append(CookieName, cookie, Options(credentials.AbsoluteExpiresAt));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
        }
    }

    public TrialBrowserTicket? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var encoded)
            || encoded.Length is < 1 or > MaximumCookieLength)
        {
            return null;
        }

        try
        {
            var cleartext = protector.Unprotect(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(encoded));
            try
            {
                var payload = JsonSerializer.Deserialize<CookiePayload>(cleartext);
                if (payload is null || payload.Version != 1 || payload.AbsoluteExpiresAt <= clock.GetUtcNow()
                    || payload.AbsoluteExpiresAt - clock.GetUtcNow() > TimeSpan.FromHours(12))
                {
                    return null;
                }

                var session = BrowserSessionToken.Parse(payload.SessionToken);
                try
                {
                    return new TrialBrowserTicket(
                        session, BrowserCsrfToken.Parse(payload.CsrfToken), payload.AbsoluteExpiresAt);
                }
                catch
                {
                    session.Dispose();
                    throw;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cleartext);
            }
        }
        catch (Exception error) when (error is CryptographicException or FormatException
            or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName, Options(null));

    private static CookieOptions Options(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt,
        IsEssential = true,
    };

    private sealed record CookiePayload(
        int Version, string SessionToken, string CsrfToken, DateTimeOffset AbsoluteExpiresAt);
}

internal sealed record TrialBrowserTicket(
    BrowserSessionToken SessionToken,
    BrowserCsrfToken CsrfToken,
    DateTimeOffset AbsoluteExpiresAt) : IDisposable
{
    public void Dispose()
    {
        SessionToken.Dispose();
        CsrfToken.Dispose();
    }

    public override string ToString() => "[redacted]";
}
