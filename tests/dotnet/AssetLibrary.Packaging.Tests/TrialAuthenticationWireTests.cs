using System.Net;
using System.Text.Json;
using AssetLibrary.AssetLink;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class TrialAuthenticationWireTests
{
    private static readonly string[] SessionFields =
    [
        "authenticated", "principal_id", "display_name", "is_system_administrator", "csrf_token", "absolute_expires_at",
    ];

    [TestMethod]
    public async Task HttpsLoginUsesProtectedHostCookieAndReturnsOnlyTheFrozenSessionFields()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        using var response = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login",
            body: JsonSerializer.Serialize(new { account_name = "trial-admin", password = TrialAuthenticationStore.Password }));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var header = response.Headers.GetValues("Set-Cookie").Single();
        StringAssert.StartsWith(header, "__Host-AssetLibrary-Session=");
        StringAssert.Contains(header, "httponly");
        StringAssert.Contains(header, "secure");
        StringAssert.Contains(header, "samesite=strict");
        StringAssert.Contains(header, "path=/");
        Assert.IsFalse(header.Contains("domain=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(header.Contains(TrialAuthenticationStore.Password, StringComparison.Ordinal));
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Origin"));
        using var session = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        CollectionAssert.AreEquivalent(SessionFields, session.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(host.Store.PrincipalId, session.RootElement.GetProperty("principal_id").GetGuid());
        Assert.IsTrue(session.RootElement.GetProperty("is_system_administrator").GetBoolean());
        var cookie = header.Split(';')[0];
        using var resumed = await host.SendAsync(HttpMethod.Get, "/assetlink/v1/auth/session", cookie, origin: null);
        Assert.AreEqual(HttpStatusCode.OK, resumed.StatusCode);
        Assert.AreEqual(2, host.Store.AuthenticationCalls);
    }

    [TestMethod]
    public async Task WrongAndUnknownCredentialsReturnTheSameGenericFailureWithoutCookies()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        string? firstBody = null;
        foreach (var account in new[] { "trial-admin", "unknown-account" })
        {
            using var response = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login",
                body: JsonSerializer.Serialize(new { account_name = account, password = "incorrect account passphrase" }));
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
            var body = await response.Content.ReadAsStringAsync();
            firstBody ??= body;
            Assert.AreEqual(firstBody, body);
            StringAssert.Contains(body, "invalid_credentials");
            Assert.IsFalse(body.Contains(account, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task OriginAndFetchMetadataRejectionsHappenBeforePasswordLookup()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        foreach (var origin in new[] { null, "null", "https://attacker.invalid", host.Origin.GetLeftPart(UriPartial.Authority) + ", https://attacker.invalid" })
        {
            using var denied = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login", body: "{}", origin: origin);
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var crossSite = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login", body: "{}", site: "cross-site");
        Assert.AreEqual(HttpStatusCode.Forbidden, crossSite.StatusCode);
        Assert.AreEqual(0, host.Store.CredentialCalls);
    }

    [TestMethod]
    public async Task CsrfAndCurrentAdministratorAreRequiredAndControlFailuresKeepAssetLinkEnvelopes()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        var session = await host.LoginAsync();
        foreach (var csrf in new[] { null, new string('a', 43), session.Csrf + "," + session.Csrf })
        {
            using var denied = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/control", session.Cookie, csrf, "{}");
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.IsFalse(denied.Headers.Contains("Set-Cookie"));
            var envelope = AssetLinkCodec.Parse(await denied.Content.ReadAsStringAsync());
            Assert.IsInstanceOfType<ErrorMessage>(envelope);
        }

        using var allowed = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/test-admin", session.Cookie, session.Csrf);
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        host.Store.Administrator = false;
        using var revokedPrivilege = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/test-admin", session.Cookie, session.Csrf);
        Assert.AreEqual(HttpStatusCode.Forbidden, revokedPrivilege.StatusCode);
        using var ordinaryRead = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/control", session.Cookie, session.Csrf);
        Assert.AreEqual(HttpStatusCode.OK, ordinaryRead.StatusCode);
    }

    [TestMethod]
    public async Task DependencyOutageDoesNotClearCookieOrPretendLogoutSucceeded()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        var session = await host.LoginAsync();
        host.Store.Available = false;
        using var unavailable = await host.SendAsync(HttpMethod.Get, "/assetlink/v1/auth/session", session.Cookie);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.IsFalse(unavailable.Headers.Contains("Set-Cookie"));
        Assert.IsFalse((await unavailable.Content.ReadAsStringAsync()).Contains("sensitive-database-details", StringComparison.Ordinal));
        using var logout = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/logout", session.Cookie, session.Csrf);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, logout.StatusCode);
        Assert.IsFalse(host.Store.Revoked);
        host.Store.Available = true;
        using var recovered = await host.SendAsync(HttpMethod.Get, "/assetlink/v1/auth/session", session.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode);
    }

    [TestMethod]
    public async Task LogoutRevokesServerSessionAndReplayedCookieIsRejectedAndRemoved()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        var session = await host.LoginAsync();
        using var logout = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/logout", session.Cookie, session.Csrf);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.IsTrue(host.Store.Revoked);
        StringAssert.Contains(logout.Headers.GetValues("Set-Cookie").Single(), "expires=");
        using var replay = await host.SendAsync(HttpMethod.Get, "/assetlink/v1/auth/session", session.Cookie);
        Assert.AreEqual(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.IsTrue(replay.Headers.Contains("Set-Cookie"));
    }

    [TestMethod]
    public async Task EverySessionRequestUsesCurrentIdentityAndRejectsCrossSiteRetrieval()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        var session = await host.LoginAsync();
        var newPrincipal = Guid.NewGuid();
        host.Store.PrincipalId = newPrincipal;
        using var current = await host.SendAsync(HttpMethod.Get, "/assetlink/v1/auth/session", session.Cookie);
        using var data = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        Assert.AreEqual(newPrincipal, data.RootElement.GetProperty("principal_id").GetGuid());
        using var crossSite = await host.SendAsync(HttpMethod.Get, "/assetlink/v1/auth/session", session.Cookie, site: "cross-site");
        Assert.AreEqual(HttpStatusCode.Forbidden, crossSite.StatusCode);
        using var wrongOrigin = await host.SendAsync(HttpMethod.Get, "/assetlink/v1/auth/session", session.Cookie,
            origin: "https://attacker.invalid");
        Assert.AreEqual(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
    }

    [TestMethod]
    public async Task MalformedOversizedAndDuplicateLoginFieldsNeverReachCredentialLookup()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        foreach (var json in new[] { "{}", "[]", "{\"account_name\":\"trial-admin\",\"password\":5}",
                     "{\"account_name\":\"trial-admin\",\"password\":\"one\",\"password\":\"two\"}" })
        {
            using var invalid = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login", body: json);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using var oversized = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login", body: new string('x', 8193));
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.AreEqual(0, host.Store.CredentialCalls);
    }
}
