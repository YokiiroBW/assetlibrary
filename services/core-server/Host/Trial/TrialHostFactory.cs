using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using AssetLibrary.CoreServer.Adapters.AssetLink;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Npgsql;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal sealed record TrialHostTestComposition(
    Func<NpgsqlDataSource, IDataProtectionProvider, ILoggerFactory, GatewayAuthenticationRuntime> Authentication,
    ReadOnlyWorkerProcessOptions Workers);

internal static class TrialHostFactory
{
    public static WebApplication Build(
        TrialConfiguration configuration,
        TrialDatabaseConnections connections,
        X509Certificate2 certificate,
        IDatabaseReadinessProbe readiness,
        TrialHostTestComposition? testComposition = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            WebRootPath = configuration.WebRoot,
        });
        CoreServerHostLogging.Configure(builder);
        TrialHttpTransport.Configure(builder, configuration, certificate);
        TrialIdentityServices.Configure(builder.Services, configuration, connections, certificate, testComposition);
        builder.Services.AddAssetLibraryReadOnlyGateway();
        ConfigureReadServices(builder.Services, configuration, connections, testComposition?.Workers ?? ReadOnlyWorkerCommand.ProcessOptions());
        builder.Services.AddSingleton<TrialManagementGateway>();
        builder.Services.AddHostedService<TrialScanWorker>();
        builder.Services.AddHostedService<TrialAvailabilityWorker>();
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

        var application = builder.Build();
        TrialWebEndpoints.Configure(application, configuration, readiness);
        CoreServerHostLogging.RegisterLifecycle(application, configuration.Origin.Port);
        return application;
    }

    private static void ConfigureReadServices(IServiceCollection services, TrialConfiguration configuration,
        TrialDatabaseConnections connections, ReadOnlyWorkerProcessOptions workers)
    {
        var libraries = TrialLibraryComposition.Create(configuration, connections, workers);
        services.AddSingleton(libraries);
        services.AddSingleton(libraries.Registration);
        services.AddSingleton(libraries.Availability);
        services.AddSingleton<IInitialScanCoordinator>(provider =>
            TrialScanComposition.Create(connections, libraries, workers, provider.GetRequiredService<ILoggerFactory>()));
    }

}
