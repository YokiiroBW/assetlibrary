using System.Net;
using System.Text.Json.Nodes;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class TransportTests
{
    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(404)]
    public async Task RejectStatusDoesNotReadUntrustedBody(int status)
    {
        using var handler = new ProtocolFixture((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StallContent() }));
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromMilliseconds(50));
        var failure = await Assert.ThrowsExactlyAsync<ClientException>(() => transport.RequestAsync("libraries.list", [], CancellationToken.None));
        Assert.AreEqual(status, failure.Status);
        Assert.IsTrue(failure.ClearsData);
    }

    [TestMethod]
    public async Task MismatchedResponseAndUnknownEnvelopeAreRejected()
    {
        using var handler = new ProtocolFixture((_, _) => Task.FromResult(ProtocolFixture.Json(new JsonObject
        { ["message_type"] = "future.result", ["request_id"] = "wrong" })));
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(1));
        var failure = await Assert.ThrowsExactlyAsync<ClientException>(() => transport.RequestAsync("libraries.list", [], CancellationToken.None));
        Assert.AreEqual("invalid_response", failure.Code);
    }

    [TestMethod]
    public async Task TimeoutAndCallerCancellationRemainDistinct()
    {
        using var handler = new ProtocolFixture(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK); });
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromMilliseconds(40));
        var failure = await Assert.ThrowsExactlyAsync<ClientException>(() => transport.RequestAsync("libraries.list", [], CancellationToken.None));
        Assert.AreEqual("timeout", failure.Code);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => transport.RequestAsync("libraries.list", [], cancelled.Token));
    }

    private sealed class StallContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new InvalidOperationException("The legacy body reader must not be used."));
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
