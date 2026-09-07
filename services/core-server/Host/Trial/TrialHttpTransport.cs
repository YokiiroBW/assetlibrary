using System.Security.Cryptography.X509Certificates;
using AssetLibrary.CoreServer.Adapters.AssetLink;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialHttpTransport
{
    public static void Configure(WebApplicationBuilder builder, TrialConfiguration configuration, X509Certificate2 certificate)
    {
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.AddServerHeader = false;
            server.Limits.MaxRequestBodySize = ReadOnlyAssetLinkEndpoints.MaximumRequestBytes;
            server.Limits.MaxConcurrentConnections = 64;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            server.Listen(configuration.BindAddress, configuration.Origin.Port, endpoint =>
            {
                endpoint.Protocols = HttpProtocols.Http1AndHttp2;
                endpoint.UseHttps(certificate);
            });
        });
    }
}
