using System.Security.Cryptography.X509Certificates;
using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.IntegrationTestSupport;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AssetLibrary.WebGateway.Tests;

internal sealed class TrialHostIntegrationFixture : IAsyncDisposable
{
    private WebApplication application;
    private readonly X509Certificate2 certificate;
    private readonly TrialDatabaseConnections connections;
    private readonly PostgresDatabaseReadinessProbe readiness;
    private readonly TrialHostTestComposition composition;
    private readonly string? ownedTlsKeyContainer;

    private TrialHostIntegrationFixture(TrialConfiguration configuration, TrialDatabaseConnections connections,
        X509Certificate2 certificate, PostgresDatabaseReadinessProbe readiness,
        TrialHostIntegrationAuthentication authentication, ReadOnlyWorkerProcessOptions workers)
    {
        Configuration = configuration;
        this.connections = connections;
        this.certificate = certificate;
        this.readiness = readiness;
        ownedTlsKeyContainer = TrialHostIntegrationCertificateContainer.Observe(certificate);
        Authentication = authentication;
        composition = new TrialHostTestComposition(authentication.Create, workers);
        application = BuildApplication();
        Client = TrialTestTls.Client(certificate, configuration.Origin);
    }

    public TrialConfiguration Configuration { get; }
    public TrialHostIntegrationAuthentication Authentication { get; }
    public HttpClient Client { get; }
    public X509Certificate2 Certificate => certificate;
    public GatewayAuthenticationRuntime Runtime => application.Services.GetRequiredService<GatewayAuthenticationRuntime>();
    public IServiceProvider Services => application.Services;

    public static async Task<TrialHostIntegrationFixture> CreateAsync(TrialHostIntegrationSettings settings)
    {
        var configuration = await TrialHostIntegrationConfiguration.CreateAsync(settings);
        var certificate = await TrialCertificate.LoadAsync(configuration, CancellationToken.None);
        TrialDatabaseConnections? connections = null;
        PostgresDatabaseReadinessProbe? readiness = null;
        TrialHostIntegrationAuthentication? authentication = null;
        try
        {
            connections = await TrialDatabaseConnections.CreateAsync(configuration.Database, CancellationToken.None);
            readiness = PostgresDatabaseReadinessProbe.Create(connections.Audit);
            await TrialHostIntegrationReadiness.AssertReadyAsync(readiness, connections);
            authentication = new TrialHostIntegrationAuthentication(
                new GatewayAuthorizationConfiguration(configuration.AuthorizationKeyFile, configuration.DeploymentId));
            return new TrialHostIntegrationFixture(configuration, connections, certificate, readiness, authentication,
                new ReadOnlyWorkerProcessOptions(settings.Dotnet, [settings.HostDll]));
        }
        catch
        {
            authentication?.Dispose();
            if (readiness is not null) await readiness.DisposeAsync();
            if (connections is not null) await connections.DisposeAsync();
            certificate.Dispose();
            throw;
        }
    }

    public Task StartAsync() => application.StartAsync();

    public async Task RestartAsync()
    {
        await application.StopAsync();
        await application.DisposeAsync();
        application = BuildApplication();
        await application.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await application.StopAsync();
            await application.DisposeAsync();
        }
        finally
        {
            Client.Dispose();
            Authentication.Dispose();
            await readiness.DisposeAsync();
            await connections.DisposeAsync();
            certificate.Dispose();
            TrialHostIntegrationCertificateContainer.AssertRemoved(ownedTlsKeyContainer);
        }
    }

    private WebApplication BuildApplication() => TrialHostFactory.Build(Configuration, connections, certificate,
        new TrialRuntimeReadinessProbe(readiness, connections), composition);
}
