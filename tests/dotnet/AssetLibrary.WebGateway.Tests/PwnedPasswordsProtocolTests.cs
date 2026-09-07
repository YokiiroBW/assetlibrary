using System.Net;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class PwnedPasswordQueryTests
{
    [TestMethod]
    public void QueryMatchesTheOfficialSha1RangeShapeAndDisposesTheSuffix()
    {
        using var secret = new LocalSecret("password".AsSpan());
        var query = PwnedPasswordQuery.Create(secret);

        Assert.AreEqual("5BAA6", query.Prefix);
        Assert.AreEqual("1E4C9B93F3F0682250B6CF8331B7EE68FD8", new string(query.Suffix));

        query.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = query.Suffix.Length);
    }

    [TestMethod]
    public void QueryUsesStrictUtf8ForUnicodeSecrets()
    {
        using var secret = new LocalSecret("密码🔐-Unicode".AsSpan());
        using var query = PwnedPasswordQuery.Create(secret);

        Assert.AreEqual("4294A", query.Prefix);
        Assert.AreEqual("329B0E8C2E445C7D677D03B8460163BF4D7", new string(query.Suffix));
    }
}

[TestClass]
public sealed class PwnedPasswordsProtocolTests
{
    [TestMethod]
    public async Task RequestSendsOnlyTheFiveCharacterPrefixAndRequiredLowSensitivityHeaders()
    {
        const string plaintext = "password";
        const string fullHash = "5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8";
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(plaintext, matchingCount: 42))));
        using var checker = PwnedPasswordsTestData.Checker(handler);
        using var secret = new LocalSecret(plaintext.AsSpan());

        var result = await checker.EvaluateAsync(secret, CancellationToken.None);

        Assert.AreEqual(LocalSecretRisk.Compromised, result);
        Assert.AreEqual(1, handler.Calls);
        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual(
            "https://api.pwnedpasswords.com/range/5BAA6",
            request.Uri?.AbsoluteUri);
        StringAssert.Contains(request.Headers, "Add-Padding: true");
        StringAssert.Contains(request.Headers, "User-Agent: AssetLibrary-GatewayAuth/0.1");
        Assert.IsFalse(request.HasContent);
        Assert.IsFalse(request.Uri!.AbsolutePath.Contains(plaintext, StringComparison.Ordinal));
        Assert.IsFalse(request.Headers.Contains(plaintext, StringComparison.Ordinal));
        Assert.IsFalse(request.Uri.AbsoluteUri.Contains(fullHash, StringComparison.Ordinal));
        Assert.IsFalse(request.Headers.Contains(fullHash, StringComparison.Ordinal));
        Assert.IsFalse(request.Headers.Contains("hibp-api-key", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ValidMissingSuffixIsAllowedAndZeroCountPaddingDoesNotCompromise()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse("first synthetic secret")),
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(
                    "second synthetic secret",
                    matchingCount: 0)),
        ]);
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(responses.Dequeue()));
        using var checker = PwnedPasswordsTestData.Checker(handler);
        using var first = new LocalSecret("first synthetic secret".AsSpan());
        using var second = new LocalSecret("second synthetic secret".AsSpan());

        Assert.AreEqual(
            LocalSecretRisk.Allowed,
            await checker.EvaluateAsync(first, CancellationToken.None));
        Assert.AreEqual(
            LocalSecretRisk.Allowed,
            await checker.EvaluateAsync(second, CancellationToken.None));
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task ProductionDefaultsRequireTheDocumentedPaddedPopulation()
    {
        const string secretText = "production padding synthetic secret";
        var tooSmallHandler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(
                    secretText,
                    matchingCount: 3,
                    lineCount: 799))));
        using var tooSmallChecker = PwnedPasswordsTestData.Checker(
            tooSmallHandler,
            new PwnedPasswordsSecretRiskOptions());
        using var secret = new LocalSecret(secretText.AsSpan());

        Assert.AreEqual(
            LocalSecretRisk.Unavailable,
            await tooSmallChecker.EvaluateAsync(secret, CancellationToken.None));

        var paddedHandler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(
                    secretText,
                    matchingCount: 3,
                    lineCount: 800))));
        using var paddedChecker = PwnedPasswordsTestData.Checker(
            paddedHandler,
            new PwnedPasswordsSecretRiskOptions());

        Assert.AreEqual(
            LocalSecretRisk.Compromised,
            await paddedChecker.EvaluateAsync(secret, CancellationToken.None));
    }

    [TestMethod]
    public async Task RedirectIsUnavailableAndIsNotFollowedByTheChecker()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("https://example.invalid/capture");
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(redirect));
        using var checker = PwnedPasswordsTestData.Checker(handler);
        using var secret = new LocalSecret("redirect synthetic secret".AsSpan());

        var result = await checker.EvaluateAsync(secret, CancellationToken.None);

        Assert.AreEqual(LocalSecretRisk.Unavailable, result);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public void ProductionTransportDisablesRedirectsCookiesAndDecompression()
    {
        var options = new PwnedPasswordsSecretRiskOptions();
        using var handler = PwnedPasswordsHttpClientFactory.CreateHandler(options);

        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.IsFalse(handler.UseCookies);
        Assert.AreEqual(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.AreEqual(options.RequestTimeout, handler.ConnectTimeout);
        Assert.AreEqual(4, handler.MaxConnectionsPerServer);
        Assert.AreEqual(16, handler.MaxResponseHeadersLength);
    }

    [TestMethod]
    public void UnsafeOptionsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new PwnedPasswordsSecretRiskOptions(requestTimeout: TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new PwnedPasswordsSecretRiskOptions(requestTimeout: TimeSpan.FromSeconds(5)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new PwnedPasswordsSecretRiskOptions(cacheLifetime: TimeSpan.FromHours(2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new PwnedPasswordsSecretRiskOptions(cacheCapacity: 257));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new PwnedPasswordsSecretRiskOptions(maximumResponseBytes: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new PwnedPasswordsSecretRiskOptions(
                minimumResponseLines: 799));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new PwnedPasswordsSecretRiskOptions(
                maximumResponseLines: PwnedPasswordsSecretRiskOptions.AbsoluteMaximumResponseLines + 1));
    }
}
