using System.Net;
using AssetLibrary.CoreServer.Adapters.AssetLink;
using AssetLibrary.Modules.GatewayAuth.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.WebGateway.Tests;

internal sealed class EndpointTestHost : IAsyncDisposable
{
    private readonly WebApplication application;

    private EndpointTestHost(WebApplication application)
    {
        this.application = application;
        var server = application.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Client = new HttpClient { BaseAddress = new Uri(address, UriKind.Absolute) };
    }

    public HttpClient Client { get; }

    public static async Task<EndpointTestHost> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton<IAuthorizedReadModelQuery>(new FakeAuthorizedReadModelQuery());
        builder.Services.AddSingleton<ReadOnlyBrowseService>();
        builder.Services.AddSingleton<ReadOnlyAssetLinkProtocol>();
        var application = builder.Build();
        application.MapAssetLibraryReadOnlyGateway();
        await application.StartAsync();
        return new EndpointTestHost(application);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await application.DisposeAsync();
    }
}
