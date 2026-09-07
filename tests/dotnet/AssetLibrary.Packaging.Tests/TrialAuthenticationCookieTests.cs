using System.Security.Cryptography;
using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class TrialAuthenticationCookieTests
{
    [TestMethod]
    public void CookieRoundTripsOpaqueTokensAndRejectsTamperingOtherKeyAndExpiry()
    {
        var clock = new TrialClock(DateTimeOffset.UtcNow);
        var codec = new TrialBrowserCookieCodec(new EphemeralDataProtectionProvider(), clock);
        using var credentials = Credentials(clock.GetUtcNow());
        var response = new DefaultHttpContext();
        codec.Write(response.Response, credentials);
        var cookie = response.Response.Headers.SetCookie.ToString().Split(';')[0];
        var request = new DefaultHttpContext();
        request.Request.Headers.Cookie = cookie;
        using (var ticket = codec.Read(request.Request))
        {
            Assert.IsNotNull(ticket);
            Assert.AreEqual(credentials.SessionToken.Export(), ticket.SessionToken.Export());
            Assert.AreEqual(credentials.CsrfToken.Export(), ticket.CsrfToken.Export());
        }

        var other = new TrialBrowserCookieCodec(new EphemeralDataProtectionProvider(), clock);
        Assert.IsNull(other.Read(request.Request));
        request.Request.Headers.Cookie = cookie[..^4] + "abcd";
        Assert.IsNull(codec.Read(request.Request));
        request.Request.Headers.Cookie = cookie;
        clock.Current = clock.Current.AddHours(13);
        Assert.IsNull(codec.Read(request.Request));
    }

    [TestMethod]
    public void RequestTrustRejectsHostHttpAndAmbiguousOrigin()
    {
        var trust = new TrialRequestTrust(new Uri("https://localhost:7443"));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost", 7443);
        context.Request.Headers.Origin = "https://localhost:7443";
        Assert.IsTrue(trust.Allows(context.Request));
        context.Request.Headers.Origin = new[] { "https://localhost:7443", "https://localhost:7443" };
        Assert.IsFalse(trust.Allows(context.Request));
        context.Request.Headers.Origin = "https://localhost:7443";
        context.Request.Host = new HostString("attacker.invalid", 7443);
        Assert.IsFalse(trust.Allows(context.Request));
        context.Request.Host = new HostString("localhost", 7443);
        context.Request.Scheme = "http";
        Assert.IsFalse(trust.Allows(context.Request));
    }

    private static BrowserSessionCredentials Credentials(DateTimeOffset now) => new(
        BrowserSessionToken.Parse(Encode(RandomNumberGenerator.GetBytes(32))),
        BrowserCsrfToken.Parse(Encode(RandomNumberGenerator.GetBytes(32))),
        now, now.AddMinutes(30), now.AddHours(12));

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class TrialClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Current;
    }
}
