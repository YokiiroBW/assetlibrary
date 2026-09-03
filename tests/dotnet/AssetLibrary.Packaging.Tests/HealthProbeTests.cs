using System.Net;
using System.Text;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class HealthProbeTests
{
    [TestMethod]
    public async Task AcceptsOnlyMatchingBoundedHealthContract()
    {
        var options = ProbeOptions();
        using var valid = new StubHandler(HttpStatusCode.OK, "{\"status\":\"ok\",\"contract\":\"v01-008/1\"}");
        using var wrongContract = new StubHandler(HttpStatusCode.OK, "{\"status\":\"ok\",\"contract\":\"other\"}");
        using var wrongStatus = new StubHandler(HttpStatusCode.ServiceUnavailable, "{}");

        Assert.IsTrue(await CoreServerHealthProbe.RunAsync(options, valid, CancellationToken.None));
        Assert.IsFalse(await CoreServerHealthProbe.RunAsync(options, wrongContract, CancellationToken.None));
        Assert.IsFalse(await CoreServerHealthProbe.RunAsync(options, wrongStatus, CancellationToken.None));
        Assert.AreEqual(IPAddress.Loopback, valid.LastRequest!.RequestUri is { } uri
            ? IPAddress.Parse(uri.Host)
            : null);
    }

    [TestMethod]
    public async Task RejectsMalformedAndOversizeResponses()
    {
        var options = ProbeOptions();
        using var malformed = new StubHandler(HttpStatusCode.OK, "not-json");
        using var wrongTypes = new StubHandler(
            HttpStatusCode.OK,
            "{\"status\":42,\"contract\":false}");
        using var oversize = new StubHandler(HttpStatusCode.OK, new string('x', 4097));

        Assert.IsFalse(await CoreServerHealthProbe.RunAsync(options, malformed, CancellationToken.None));
        Assert.IsFalse(await CoreServerHealthProbe.RunAsync(options, wrongTypes, CancellationToken.None));
        Assert.IsFalse(await CoreServerHealthProbe.RunAsync(options, oversize, CancellationToken.None));
    }

    [TestMethod]
    public async Task CancellationAndTransportFailureRemainUnhealthy()
    {
        var options = ProbeOptions();
        using var cancelled = new StubHandler(HttpStatusCode.OK, "{}", waitForCancellation: true);
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.IsFalse(await CoreServerHealthProbe.RunAsync(options, cancelled, source.Token));
        using var failed = new StubHandler(new HttpRequestException("fixture"));
        Assert.IsFalse(await CoreServerHealthProbe.RunAsync(options, failed, CancellationToken.None));
    }

    private static CoreServerHostOptions ProbeOptions() => new(
        CoreServerHostCommand.HealthProbe,
        string.Empty,
        string.Empty,
        IPAddress.Loopback,
        IPAddress.Loopback,
        CoreServerHostOptions.DefaultPort);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;
        private readonly string body;
        private readonly bool waitForCancellation;
        private readonly Exception? failure;

        public StubHandler(HttpStatusCode statusCode, string body, bool waitForCancellation = false)
        {
            this.statusCode = statusCode;
            this.body = body;
            this.waitForCancellation = waitForCancellation;
        }

        public StubHandler(Exception failure)
        {
            this.failure = failure;
            body = string.Empty;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (failure is not null)
            {
                throw failure;
            }

            if (waitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
