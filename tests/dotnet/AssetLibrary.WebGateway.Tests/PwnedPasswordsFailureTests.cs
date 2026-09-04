using System.Net;
using System.Text;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class PwnedPasswordsFailureTests
{
    private const string SecretText = "failure boundary synthetic secret";

    [TestMethod]
    public async Task EveryNonSuccessStatusFailsClosed()
    {
        foreach (var status in new[]
                 {
                     HttpStatusCode.BadRequest,
                     HttpStatusCode.Forbidden,
                     HttpStatusCode.NotFound,
                     HttpStatusCode.TooManyRequests,
                     HttpStatusCode.InternalServerError,
                     HttpStatusCode.ServiceUnavailable,
                 })
        {
            var handler = new RecordingHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(status)));
            using var checker = PwnedPasswordsTestData.Checker(handler);
            using var secret = new LocalSecret(SecretText.AsSpan());

            Assert.AreEqual(
                LocalSecretRisk.Unavailable,
                await checker.EvaluateAsync(secret, CancellationToken.None),
                status.ToString());
        }
    }

    [TestMethod]
    public async Task MalformedTextResponsesFailClosed()
    {
        var validSuffix = new string('A', PwnedPasswordQuery.SuffixCharacters);
        var malformedBodies = new[]
        {
            validSuffix + ":1",
            validSuffix + ":1\n",
            new string('a', PwnedPasswordQuery.SuffixCharacters) + ":1\r\n",
            new string('A', PwnedPasswordQuery.SuffixCharacters - 1) + ":1\r\n",
            validSuffix + " 1\r\n",
            validSuffix + ":\r\n",
            validSuffix + ":-1\r\n",
            validSuffix + ":+1\r\n",
            validSuffix + ":1 \r\n",
            validSuffix + ":9223372036854775808\r\n",
            validSuffix + ":1\r\n" + validSuffix + ":1\r\n",
            validSuffix + ":0\r\n" + validSuffix + ":1\r\n",
        };

        foreach (var body in malformedBodies)
        {
            var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
                PwnedPasswordsTestData.TextResponse(body)));
            using var checker = PwnedPasswordsTestData.Checker(handler);
            using var secret = new LocalSecret(SecretText.AsSpan());

            Assert.AreEqual(
                LocalSecretRisk.Unavailable,
                await checker.EvaluateAsync(secret, CancellationToken.None),
                body.Replace("\r", "\\r", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task InvalidUtf8TooFewAndTooManyLinesFailClosed()
    {
        var invalidUtf8 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0xc3, 0x28]),
        };
        await AssertUnavailableAsync(invalidUtf8);

        var tooFew = PwnedPasswordsTestData.ValidResponse(SecretText, lineCount: 799);
        await AssertUnavailableAsync(PwnedPasswordsTestData.TextResponse(tooFew));

        var tooMany = PwnedPasswordsTestData.ValidResponse(SecretText, lineCount: 1_201);
        await AssertUnavailableAsync(PwnedPasswordsTestData.TextResponse(tooMany));
    }

    [TestMethod]
    public async Task DeclaredAndStreamedResponseBoundsFailClosed()
    {
        var empty = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        await AssertUnavailableAsync(empty);

        var declaredOversize = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(
                new byte[PwnedPasswordsSecretRiskOptions.DefaultMaximumResponseBytes + 1]),
        };
        await AssertUnavailableAsync(declaredOversize);

        var streamedOversizeContent = new StreamContent(new MemoryStream(
            new byte[PwnedPasswordsSecretRiskOptions.DefaultMaximumResponseBytes + 1]));
        streamedOversizeContent.Headers.ContentLength = null;
        var streamedOversize = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = streamedOversizeContent,
        };
        await AssertUnavailableAsync(streamedOversize);

        var validBody = Encoding.UTF8.GetBytes(
            PwnedPasswordsTestData.ValidResponse(SecretText));
        var truncatedContent = new ByteArrayContent(validBody);
        truncatedContent.Headers.ContentLength = validBody.Length + 1;
        var truncated = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = truncatedContent,
        };
        await AssertUnavailableAsync(truncated);
    }

    private static async Task AssertUnavailableAsync(
        HttpResponseMessage response)
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(response));
        using var checker = PwnedPasswordsTestData.Checker(handler);
        using var secret = new LocalSecret(SecretText.AsSpan());

        Assert.AreEqual(
            LocalSecretRisk.Unavailable,
            await checker.EvaluateAsync(secret, CancellationToken.None));
    }
}

[TestClass]
public sealed class PwnedPasswordsTransportFailureTests
{
    private const string SecretText = "failure boundary synthetic secret";

    [TestMethod]
    public async Task TransportAndResponseReadFailuresFailClosed()
    {
        var transportFailure = new RecordingHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("synthetic sensitive transport detail")));
        using (var checker = PwnedPasswordsTestData.Checker(transportFailure))
        using (var secret = new LocalSecret(SecretText.AsSpan()))
        {
            Assert.AreEqual(
                LocalSecretRisk.Unavailable,
                await checker.EvaluateAsync(secret, CancellationToken.None));
        }

        var readFailure = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ThrowingReadStream()),
            }));
        using (var checker = PwnedPasswordsTestData.Checker(readFailure))
        using (var secret = new LocalSecret(SecretText.AsSpan()))
        {
            Assert.AreEqual(
                LocalSecretRisk.Unavailable,
                await checker.EvaluateAsync(secret, CancellationToken.None));
        }
    }

    [TestMethod]
    [Timeout(5_000, CooperativeCancellation = true)]
    public async Task ProviderTimeoutMapsToUnavailable()
    {
        var handler = new RecordingHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertFailedException("The timeout should cancel the transport.");
        });
        using var checker = PwnedPasswordsTestData.Checker(
            handler,
            PwnedPasswordsTestData.Options(requestTimeout: TimeSpan.FromMilliseconds(30)));
        using var secret = new LocalSecret(SecretText.AsSpan());

        Assert.AreEqual(
            LocalSecretRisk.Unavailable,
            await checker.EvaluateAsync(secret, CancellationToken.None));
    }

    [TestMethod]
    [Timeout(5_000, CooperativeCancellation = true)]
    public async Task CallerCancellationPropagatesInsteadOfBecomingUnavailable()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertFailedException("Caller cancellation should cancel the transport.");
        });
        using var checker = PwnedPasswordsTestData.Checker(handler);
        using var secret = new LocalSecret(SecretText.AsSpan());
        using var cancellation = new CancellationTokenSource();

        var evaluation = checker.EvaluateAsync(secret, cancellation.Token).AsTask();
        await entered.Task;
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await evaluation);
    }
}
