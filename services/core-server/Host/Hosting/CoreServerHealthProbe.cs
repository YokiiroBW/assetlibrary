using System.Net;
using System.Text.Json;

namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerHealthProbe
{
    private const int MaximumResponseBytes = 4096;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static async Task<bool> RunAsync(
        CoreServerHostOptions options,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        var builder = new UriBuilder(Uri.UriSchemeHttp, options.ProbeAddress.ToString(), options.Port, "healthz");
        try
        {
            using var response = await client.GetAsync(
                builder.Uri,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(stream, deadline.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            return root.TryGetProperty("status", out var status)
                && status.GetString() == "ok"
                && root.TryGetProperty("contract", out var contract)
                && contract.GetString() == CoreServerBuildInfo.CurrentContract;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or IOException
            or InvalidOperationException
            or JsonException
            or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaximumResponseBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total > MaximumResponseBytes)
        {
            throw new IOException("Health response exceeded the bounded contract.");
        }

        return buffer[..total];
    }
}
