using System.Text;
using AssetLibrary.CoreServer.Adapters.AssetLink;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialWebEndpoints
{
    public static void Configure(WebApplication application, TrialConfiguration configuration, IDatabaseReadinessProbe readiness)
    {
        application.Use(async (context, next) =>
        {
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; object-src 'none'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            await next(context).ConfigureAwait(false);
        });
        TrialAuthentication.Use(application);
        TrialAuthentication.Map(application);
        application.MapAssetLibraryControl((context, payload, token) =>
            context.RequestServices.GetRequiredService<TrialManagementGateway>().HandleAsync(context, payload, token));
        TrialReadinessEndpoint.Map(application, configuration.DeploymentId, readiness);
        application.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = "no-store";
            },
        });
        application.MapGet("/", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(Path.Combine(configuration.WebRoot, "index.html"), context.RequestAborted).ConfigureAwait(false);
        });
    }
}
