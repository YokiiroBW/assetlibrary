using System.Net;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PwnedPasswordsHttpClientFactory
{
    public static HttpClient Create(PwnedPasswordsSecretRiskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new HttpClient(CreateHandler(options), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    internal static SocketsHttpHandler CreateHandler(PwnedPasswordsSecretRiskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = options.RequestTimeout,
            MaxConnectionsPerServer = 4,
            MaxResponseHeadersLength = 16,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            UseCookies = false,
        };
    }
}
