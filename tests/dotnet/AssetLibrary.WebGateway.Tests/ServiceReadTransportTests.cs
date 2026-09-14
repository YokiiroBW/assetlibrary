using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.CoreServer.Hosting.Trial;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class ServiceReadTransportTests
{
    [TestMethod]
    public async Task DisabledHttpWrongAuthorityPathAndBrowserHeadersNeverReachAuthenticationOrDispatch()
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var configuration = Configuration();
        Action<DefaultHttpContext>[] changes =
        [
            context => context.Request.Scheme = "http",
            context => context.Request.Host = new HostString("wrong.invalid", 8443),
            context => context.Request.Path = "/assetlink/v1/auth/session",
            context => context.Request.Method = "GET",
            context => context.Request.Headers.Cookie = "",
            context => context.Request.Headers.Origin = "",
            context => context.Request.Headers["Sec-Fetch-Dest"] = "empty",
            context => context.Request.Headers[TrialRequestTrust.CsrfHeader] = "anything",
        ];
        foreach (var change in changes)
        {
            var context = Context(services);
            change(context);
            await RejectAsync(new ServiceReadAuthenticationHandler(configuration, null!), context);
        }
        await RejectAsync(new ServiceReadAuthenticationHandler(configuration with { ServiceReadEnabled = false }, null!), Context(services));
    }

    private static async Task RejectAsync(ServiceReadAuthenticationHandler handler, DefaultHttpContext context)
    {
        await handler.InvokeAsync(context, _ => throw new AssertFailedException("Rejected transport was dispatched."));
        Assert.AreEqual(403, context.Response.StatusCode);
        Assert.IsFalse(ServiceReadAuthenticationHandler.IsServiceRequest(context));
        Assert.IsFalse(context.User.Identity!.IsAuthenticated);
    }

    private static DefaultHttpContext Context(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost", 8443);
        context.Request.Method = "POST";
        context.Request.Path = "/assetlink/v1/control";
        context.Request.Headers.Authorization = "Bearer " + new string('A', 43);
        return context;
    }

    private static TrialConfiguration Configuration() => new()
    {
        FormatVersion = 1,
        DeploymentId = Guid.NewGuid(),
        PublicOrigin = "https://localhost:8443",
        StatePath = "unused",
        TlsCertificateFile = "unused",
        TlsCertificatePasswordFile = "unused",
        DataProtectionPath = "unused",
        AuthorizationKeyFile = "unused",
        WebRoot = "unused",
        Database = null!,
        StorageSources = [],
        ServiceReadEnabled = true,
    };
}
