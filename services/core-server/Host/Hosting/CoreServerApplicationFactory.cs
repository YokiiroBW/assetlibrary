using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerApplicationFactory
{
    public static WebApplication Build(CoreServerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = CoreServerWebApplicationBuilder.Create(options.EnvironmentName);
        builder.Host.UseWindowsService(service => service.ServiceName = "AssetLibrary Core Server");
        CoreServerHostLogging.Configure(builder);
        builder.WebHost.ConfigureKestrel(server => server.Listen(
            options.BindAddress,
            options.Port,
            endpoint => endpoint.Protocols = HttpProtocols.Http1));
        builder.Services.AddSingleton(options);
        CoreServerHttpJson.Configure(builder.Services);

        var application = builder.Build();
        application.MapGet("/healthz", CoreServerEndpointPayloads.Health);
        application.MapGet("/readyz", CoreServerEndpointPayloads.Ready);
        CoreServerHostLogging.RegisterLifecycle(application, options.Port);
        return application;
    }
}
