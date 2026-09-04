using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;

namespace AssetLibrary.WebGateway.Tests;

internal static class PwnedPasswordsTestData
{
    public static PwnedPasswordsSecretRiskOptions Options(
        TimeSpan? requestTimeout = null,
        TimeSpan? cacheLifetime = null,
        int cacheCapacity = 4,
        int maximumResponseBytes = PwnedPasswordsSecretRiskOptions.DefaultMaximumResponseBytes,
        int minimumResponseLines = PwnedPasswordsSecretRiskOptions.DefaultMinimumResponseLines,
        int maximumResponseLines = PwnedPasswordsSecretRiskOptions.DefaultMaximumResponseLines) =>
        new(
            requestTimeout ?? TimeSpan.FromMilliseconds(500),
            cacheLifetime ?? TimeSpan.FromMinutes(1),
            cacheCapacity,
            maximumResponseBytes,
            minimumResponseLines,
            maximumResponseLines);

    public static string ValidResponse(
        string secret,
        long? matchingCount = null,
        int lineCount = PwnedPasswordsSecretRiskOptions.DefaultMinimumResponseLines)
    {
        using var localSecret = new LocalSecret(secret.AsSpan());
        using var query = PwnedPasswordQuery.Create(localSecret);
        var soughtSuffix = new string(query.Suffix);
        var lines = new List<string>(lineCount);
        if (matchingCount is long count)
        {
            lines.Add(soughtSuffix + ":" + count.ToString(CultureInfo.InvariantCulture));
        }

        for (var index = 1; lines.Count < lineCount; index++)
        {
            var candidate = index.ToString("X35", CultureInfo.InvariantCulture);
            if (!string.Equals(candidate, soughtSuffix, StringComparison.Ordinal))
            {
                lines.Add(candidate + ":0");
            }
        }

        return string.Concat(lines.Select(static line => line + "\r\n"));
    }

    public static HttpResponseMessage TextResponse(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain"),
        };
    }

    public static PwnedPasswordsSecretRiskChecker Checker(
        RecordingHttpMessageHandler handler,
        PwnedPasswordsSecretRiskOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        var client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new PwnedPasswordsSecretRiskChecker(
            client,
            options ?? Options(),
            timeProvider);
    }
}

internal sealed record PwnedPasswordsRequestSnapshot(
    Uri? Uri,
    HttpMethod Method,
    string Headers,
    bool HasContent);

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder;
    private readonly ConcurrentQueue<PwnedPasswordsRequestSnapshot> requests = new();
    private int calls;

    public RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        this.responder = responder ?? throw new ArgumentNullException(nameof(responder));
    }

    public int Calls => Volatile.Read(ref calls);

    public IReadOnlyList<PwnedPasswordsRequestSnapshot> Requests => requests.ToArray();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        requests.Enqueue(new PwnedPasswordsRequestSnapshot(
            request.RequestUri,
            request.Method,
            request.Headers.ToString(),
            request.Content is not null));
        return responder(request, cancellationToken);
    }
}

internal sealed class MutableGatewayTimeProvider(DateTimeOffset current) : TimeProvider
{
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => current;

    public override long GetTimestamp() => timestamp;

    public void RewindUtc(TimeSpan duration) => current = current.Subtract(duration);

    public void Advance(TimeSpan duration)
    {
        current = current.Add(duration);
        timestamp += duration.Ticks;
    }
}

internal sealed class ThrowingReadStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new IOException("synthetic read failure");

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<int>(new IOException("synthetic read failure"));

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
