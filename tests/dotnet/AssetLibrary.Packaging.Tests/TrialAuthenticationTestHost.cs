using System.Security.Cryptography.X509Certificates;
using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.Modules.GatewayAuth.Application;
using Microsoft.AspNetCore.Builder;

namespace AssetLibrary.Packaging.Tests;

internal sealed class TrialAuthenticationTestHost : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly X509Certificate2 certificate;
    private readonly LocalAuthenticationService localAuthentication;

    private TrialAuthenticationTestHost(WebApplication application, X509Certificate2 certificate,
        TrialAuthenticationStore store, LocalAuthenticationService localAuthentication, Uri origin)
    {
        this.application = application;
        this.certificate = certificate;
        this.localAuthentication = localAuthentication;
        Store = store;
        Origin = origin;
        Client = TrialAuthenticationTestTls.Client(certificate, origin);
    }

    public TrialAuthenticationStore Store { get; }

    public Uri Origin { get; }

    public HttpClient Client { get; }

    public static async Task<TrialAuthenticationTestHost> StartAsync()
    {
        var certificate = TrialAuthenticationTestTls.Certificate();
        var origin = TrialAuthenticationTestTls.ReserveOrigin();
        var store = new TrialAuthenticationStore();
        WebApplication? application = null;
        LocalAuthenticationService? authentication = null;
        try
        {
            application = TrialAuthenticationTestApplication.Build(origin, certificate, store, out authentication);
            await application.StartAsync();
            return new TrialAuthenticationTestHost(application, certificate, store, authentication, origin);
        }
        catch
        {
            try
            {
                if (application is not null)
                {
                    await application.DisposeAsync();
                }
            }
            finally
            {
                authentication?.Dispose();
                store.Dispose();
                certificate.Dispose();
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        try
        {
            await application.DisposeAsync();
        }
        finally
        {
            localAuthentication.Dispose();
            Store.Dispose();
            certificate.Dispose();
        }
    }
}
