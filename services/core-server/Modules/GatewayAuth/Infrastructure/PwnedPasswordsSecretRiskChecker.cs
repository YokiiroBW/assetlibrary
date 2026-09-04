using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PwnedPasswordsSecretRiskChecker : ILocalSecretRiskChecker, IDisposable
{
    private static readonly Uri RangeEndpoint = new(
        "https://api.pwnedpasswords.com/range/",
        UriKind.Absolute);
    private const string UserAgentProduct = "AssetLibrary-GatewayAuth";
    private const string UserAgentVersion = "0.1";

    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly PwnedPasswordsSecretRiskOptions options;
    private readonly TimeProvider timeProvider;
    private readonly PwnedPasswordsPrefixRangeCache cache;
    private bool disposed;

    public PwnedPasswordsSecretRiskChecker()
        : this(new PwnedPasswordsSecretRiskOptions())
    {
    }

    private PwnedPasswordsSecretRiskChecker(PwnedPasswordsSecretRiskOptions options)
        : this(
            PwnedPasswordsHttpClientFactory.Create(options),
            options,
            TimeProvider.System,
            ownsClient: true)
    {
    }

    internal PwnedPasswordsSecretRiskChecker(
        HttpClient client,
        PwnedPasswordsSecretRiskOptions options,
        TimeProvider? timeProvider = null)
        : this(client, options, timeProvider ?? TimeProvider.System, ownsClient: false)
    {
    }

    private PwnedPasswordsSecretRiskChecker(
        HttpClient client,
        PwnedPasswordsSecretRiskOptions options,
        TimeProvider timeProvider,
        bool ownsClient)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.ownsClient = ownsClient;
        cache = new PwnedPasswordsPrefixRangeCache(
            options.CacheCapacity,
            options.CacheLifetime,
            timeProvider);
    }

    public async ValueTask<LocalSecretRisk> EvaluateAsync(
        LocalSecret secret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var query = PwnedPasswordQuery.Create(secret);
        if (cache.TryGet(query.Prefix, out var cached))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ToRisk(cached, query.Suffix);
        }

        using var timeout = new CancellationTokenSource(options.RequestTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            var range = await FetchRangeAsync(query.Prefix, linked.Token).ConfigureAwait(false);
            if (range is null)
            {
                return LocalSecretRisk.Unavailable;
            }

            linked.Token.ThrowIfCancellationRequested();
            cache.Store(query.Prefix, range);
            return ToRisk(range, query.Suffix);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return LocalSecretRisk.Unavailable;
        }
        catch (Exception)
        {
            return LocalSecretRisk.Unavailable;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cache.Clear();
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    private static LocalSecretRisk ToRisk(
        PwnedPasswordPrefixRange range,
        ReadOnlySpan<char> suffix) =>
        range.IsCompromised(suffix)
            ? LocalSecretRisk.Compromised
            : LocalSecretRisk.Allowed;

    private async Task<PwnedPasswordPrefixRange?> FetchRangeAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(RangeEndpoint, prefix));
        request.Headers.Add("Add-Padding", "true");
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue(UserAgentProduct, UserAgentVersion));

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is < 1 || contentLength > options.MaximumResponseBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken).ConfigureAwait(false);
        var body = await PwnedPasswordsResponseReader.ReadAsync(
            stream,
            options.MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        try
        {
            if (contentLength is long expectedLength && body.Length != expectedLength)
            {
                return null;
            }

            return PwnedPasswordsRangeParser.TryParse(
                body,
                options.MinimumResponseLines,
                options.MaximumResponseLines,
                out var range)
                ? range
                : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
        }
    }
}
