using System.Net;

namespace AssetLibrary.Windows.Client;

public sealed class ThumbnailHttpException(int status) : Exception("Derived thumbnail request failed.")
{
    public int Status { get; } = status;
    public bool InvalidatesSession => Status is 401 or 403;
}

internal sealed class ThumbnailHttpReader(HttpClient client, SemaphoreSlim admission) : IDisposable
{
    internal const int MaximumBytes = 2 * 1024 * 1024;
    private readonly SemaphoreSlim imageAdmission = new(1, 1);

    internal async Task<byte[]> ReadAsync(Guid library, Guid entry, CancellationToken token)
    {
        if (library == Guid.Empty || entry == Guid.Empty) { throw new ArgumentException("Stable thumbnail IDs required."); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var entered = false;
        try
        {
            await imageAdmission.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            return await RetryAsync(library, entry, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new ThumbnailHttpException(504); }
        catch (HttpRequestException) { throw new ThumbnailHttpException(503); }
        finally { if (entered) { imageAdmission.Release(); } }
    }

    private async Task<byte[]> RetryAsync(Guid library, Guid entry, CancellationToken token)
    {
        for (var attempt = 0; ; ++attempt)
        {
            var retry = TimeSpan.Zero;
            await admission.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"assetlink/v1/libraries/{library:D}/entries/{entry:D}/image?variant=thumbnail");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    if (response.StatusCode != HttpStatusCode.OK) { throw new ThumbnailHttpException((int)response.StatusCode); }
                    return await ReadPngAsync(response, token).ConfigureAwait(false);
                }
                if (attempt == 2) { throw new ThumbnailHttpException(429); }
                retry = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1));
                if (retry < TimeSpan.Zero) { retry = TimeSpan.Zero; }
                if (retry > TimeSpan.FromSeconds(20)) { throw new ThumbnailHttpException(429); }
            }
            finally { admission.Release(); }
            await Task.Delay(retry, token).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadPngAsync(HttpResponseMessage response, CancellationToken token)
    {
        var length = response.Content.Headers.ContentLength;
        if (response.Content.Headers.ContentType?.MediaType != "image/png" || length is null or < 33 or > MaximumBytes
            || response.Content.Headers.ContentEncoding.Count != 0)
        { throw new InvalidDataException("Invalid derived PNG representation."); }
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[checked((int)length.Value)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (await stream.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0)
        { throw new InvalidDataException("Derived PNG length mismatch."); }
        return bytes;
    }

    public void Dispose() => imageAdmission.Dispose();
}
