using System.Security.Cryptography.X509Certificates;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialIdentityServices
{
    public static void Configure(IServiceCollection services, TrialConfiguration configuration,
        TrialDatabaseConnections connections, X509Certificate2 certificate, TrialHostTestComposition? testComposition,
        IReadOnlyList<X509Certificate2>? decryptionCertificates = null)
    {
        services.AddDataProtection()
            .SetApplicationName("AssetLibrary:" + configuration.DeploymentId.ToString("D"))
            .PersistKeysToFileSystem(new DirectoryInfo(configuration.DataProtectionPath))
            .ProtectKeysWithCertificate(certificate)
            .UnprotectKeysWithAnyCertificate([certificate, .. decryptionCertificates ?? []]);
        services.AddSingleton(connections.Gateway);
        services.AddSingleton(provider => testComposition is null
            ? GatewayAuthenticationComposition.Create(connections.Gateway, provider.GetRequiredService<IDataProtectionProvider>(),
                new GatewayAuthorizationConfiguration(configuration.AuthorizationKeyFile, configuration.DeploymentId),
                provider.GetRequiredService<ILoggerFactory>())
            : testComposition.Authentication(connections.Gateway, provider.GetRequiredService<IDataProtectionProvider>(),
                provider.GetRequiredService<ILoggerFactory>()));
        TrialServiceReadComposition.Configure(services, configuration, connections);
        TrialAuthentication.Configure(services, configuration.Origin);
    }
}
