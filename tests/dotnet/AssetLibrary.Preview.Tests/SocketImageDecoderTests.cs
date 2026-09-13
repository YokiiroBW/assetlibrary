using System.Diagnostics;
using System.Net.Sockets;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Modules.PreviewProvider.Contracts;
using AssetLibrary.Modules.PreviewProvider.Infrastructure;
using static AssetLibrary.Preview.Tests.SocketImageAssertions;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class SocketImageDecoderTests
{
    [TestMethod]
    [DataRow(ImagePreviewVariant.Thumbnail)]
    [DataRow(ImagePreviewVariant.Preview)]
    public async Task SendsExactSourceWithoutHalfCloseAndReturnsWithoutEof(ImagePreviewVariant variant)
    {
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            var request = await SocketImageTestServer.RequestAsync(socket, token);
            Assert.AreEqual((int)variant, request.Profile);
            Assert.IsFalse(socket.Poll(100_000, SelectMode.SelectRead), "Core must leave its send direction open.");
            await SocketImageTestServer.RespondAndObserveCloseAsync(socket, request.Profile, token);
        });
        using var decoder = new UnixSocketImageDecoder(server.PathName, static _ => true);
        var source = new SocketImageSource();
        var png = await decoder.DecodeAsync(source, variant, CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        CollectionAssert.AreEqual(SocketImageTestServer.Png, png);
        Assert.AreEqual(1, source.Copies);
        await server.CompleteAsync();
    }

    [TestMethod]
    public async Task RejectsPeerBeforeReadingOrSendingSource()
    {
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            Assert.AreEqual(0, await socket.ReceiveAsync(new byte[1], SocketFlags.None, token));
        });
        using var decoder = new UnixSocketImageDecoder(server.PathName, static _ => false);
        var source = new SocketImageSource();
        var failure = await FailureAsync(decoder, source);
        Assert.AreEqual(ImagePreviewFailure.Unavailable, failure.Failure);
        Assert.AreEqual(0, source.Copies);
        await server.CompleteAsync();
    }

    [TestMethod]
    [DataRow(1, 1, 0, 0, 0)]
    [DataRow(1, 0, 1, 0, 0)]
    [DataRow(1, 0, 0, 1, 0)]
    [DataRow(1, 0, 0, 0, 1)]
    [DataRow(99, 0, 0, 0, 0)]
    public async Task RejectsMalformedReadyBeforeSource(int status, int profile, int length, int width, int height)
    {
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(ImageWorkerProtocol.Header(status, profile, length, width, height), token);
            Assert.AreEqual(0, await stream.ReadAsync(new byte[1], token));
        });
        using var decoder = new UnixSocketImageDecoder(server.PathName, static _ => true);
        var source = new SocketImageSource();
        Assert.AreEqual(ImagePreviewFailure.Invalid, (await FailureAsync(decoder, source)).Failure);
        Assert.AreEqual(0, source.Copies);
        await server.CompleteAsync();
    }

    [TestMethod]
    public async Task BusyReadyRefusesBeforeAnySourceBytes()
    {
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(ImageWorkerProtocol.Header(7, 0, 0), token);
            Assert.AreEqual(0, await stream.ReadAsync(new byte[1], token));
        });
        using var decoder = new UnixSocketImageDecoder(server.PathName, static _ => true);
        var source = new SocketImageSource();
        Assert.AreEqual(ImagePreviewFailure.Unavailable, (await FailureAsync(decoder, source)).Failure);
        Assert.AreEqual(0, source.Copies);
        await server.CompleteAsync();
    }

    [TestMethod]
    [DataRow(4, ImagePreviewFailure.Invalid)]
    [DataRow(5, ImagePreviewFailure.Unsupported)]
    [DataRow(6, ImagePreviewFailure.LimitExceeded)]
    [DataRow(7, ImagePreviewFailure.Unavailable)]
    public async Task MapsEmptyFailureFrames(int status, ImagePreviewFailure expected)
    {
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            await SocketImageTestServer.RequestAsync(socket, token);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(ImageWorkerProtocol.Header(status, 0, 0), token);
            Assert.AreEqual(0, await stream.ReadAsync(new byte[1], token));
        });
        using var decoder = new UnixSocketImageDecoder(server.PathName, static _ => true);
        Assert.AreEqual(expected, (await FailureAsync(decoder, new SocketImageSource())).Failure);
        await server.CompleteAsync();
    }

    [TestMethod]
    [DataRow("profile", ImagePreviewFailure.Invalid)]
    [DataRow("length", ImagePreviewFailure.LimitExceeded)]
    [DataRow("png", ImagePreviewFailure.Invalid)]
    [DataRow("truncated", ImagePreviewFailure.Unavailable)]
    [DataRow("trailing", ImagePreviewFailure.Invalid)]
    [DataRow("failure-fields", ImagePreviewFailure.Invalid)]
    public async Task RejectsInvalidResponses(string corruption, ImagePreviewFailure expected)
    {
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            await SocketImageTestServer.RequestAsync(socket, token);
            var response = SocketImageTestServer.Response(corruption == "profile" ? 1 : 0, corruption == "trailing");
            if (corruption == "length") response = ImageWorkerProtocol.Header(3, 0, ImageWorkerProtocol.ThumbnailBytes + 1, 1, 1);
            if (corruption == "png") response[^1] ^= 1;
            if (corruption == "truncated") response = response[..^1];
            if (corruption == "failure-fields") response = ImageWorkerProtocol.Header(7, 0, 1);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(response, token);
        });
        using var decoder = new UnixSocketImageDecoder(server.PathName, static _ => true);
        Assert.AreEqual(expected, (await FailureAsync(decoder, new SocketImageSource())).Failure);
        await server.CompleteAsync();
    }

}

[TestClass]
public sealed class SocketImageLifecycleTests
{
    [TestMethod]
    public async Task SerialWaitCanCancelWithoutOpeningAnotherConnection()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new SocketImageTestServer(async (socket, index, token) =>
        {
            var request = await SocketImageTestServer.RequestAsync(socket, token);
            if (index == 0) { firstEntered.SetResult(); await finishFirst.Task.WaitAsync(token); }
            await SocketImageTestServer.RespondAndObserveCloseAsync(socket, request.Profile, token);
        }, 2);
        var peers = 0;
        using var decoder = new UnixSocketImageDecoder(server.PathName, _ => { Interlocked.Increment(ref peers); return true; });
        var first = decoder.DecodeAsync(new SocketImageSource(), ImagePreviewVariant.Thumbnail, CancellationToken.None).AsTask();
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var cancel = new CancellationTokenSource();
        var secondSource = new SocketImageSource();
        var second = decoder.DecodeAsync(secondSource, ImagePreviewVariant.Thumbnail, cancel.Token).AsTask();
        cancel.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => second);
        Assert.AreEqual(1, peers);
        Assert.AreEqual(0, secondSource.Copies);
        finishFirst.SetResult();
        await first;
        await decoder.DecodeAsync(new SocketImageSource(), ImagePreviewVariant.Preview, CancellationToken.None);
        await server.CompleteAsync();
    }

    [TestMethod]
    public async Task QueueWaitConsumesTheOriginalEightSecondBudget()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new SocketImageTestServer(async (socket, index, token) =>
        {
            if (index == 1)
            {
                Assert.AreEqual(0, await socket.ReceiveAsync(new byte[1], SocketFlags.None, token));
                return;
            }
            var request = await SocketImageTestServer.RequestAsync(socket, token);
            entered.SetResult(); await finish.Task.WaitAsync(token);
            await SocketImageTestServer.RespondAndObserveCloseAsync(socket, request.Profile, token);
        }, 2);
        using var decoder = new UnixSocketImageDecoder(server.PathName, static _ => true);
        var first = decoder.DecodeAsync(new SocketImageSource(), ImagePreviewVariant.Thumbnail, CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var watch = Stopwatch.StartNew();
        var second = FailureAsync(decoder, new SocketImageSource());
        await Task.Delay(TimeSpan.FromSeconds(6)); finish.SetResult(); await first;
        Assert.AreEqual(ImagePreviewFailure.Timeout, (await second.WaitAsync(TimeSpan.FromSeconds(4))).Failure);
        Assert.IsTrue(watch.Elapsed >= TimeSpan.FromSeconds(7) && watch.Elapsed < TimeSpan.FromSeconds(10));
        await server.CompleteAsync();
    }

    [TestMethod]
    public async Task DisposeDisconnectsInflightAndWaitingCallsWithoutDisposingTheirSemaphoreEarly()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            await SocketImageTestServer.RequestAsync(socket, token); entered.SetResult();
            Assert.AreEqual(0, await socket.ReceiveAsync(new byte[1], SocketFlags.None, token));
        });
        var decoder = new UnixSocketImageDecoder(server.PathName, static _ => true);
        var first = FailureAsync(decoder, new SocketImageSource());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = FailureAsync(decoder, new SocketImageSource());
        var watch = Stopwatch.StartNew(); decoder.Dispose(); decoder.Dispose();
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(1), "Dispose must not join the eight-second exchange.");
        Assert.AreEqual(ImagePreviewFailure.Unavailable, (await first).Failure);
        Assert.AreEqual(ImagePreviewFailure.Unavailable, (await second).Failure);
        Assert.AreEqual(ImagePreviewFailure.Unavailable, (await FailureAsync(decoder, new SocketImageSource())).Failure);
        await server.CompleteAsync();
    }

    [TestMethod]
    public async Task CallerCancellationClosesAnInflightConnection()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            await SocketImageTestServer.RequestAsync(socket, token); entered.SetResult();
            Assert.AreEqual(0, await socket.ReceiveAsync(new byte[1], SocketFlags.None, token));
        });
        using var decoder = new UnixSocketImageDecoder(server.PathName, static _ => true);
        using var cancel = new CancellationTokenSource();
        var request = decoder.DecodeAsync(new SocketImageSource(), ImagePreviewVariant.Thumbnail, cancel.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancel.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => request);
        await server.CompleteAsync();
    }

    [TestMethod]
    public async Task SourceChangeIsNotReclassifiedAsTransportFailure()
    {
        await using var server = new SocketImageTestServer(async (socket, _, token) =>
        {
            await SocketImageTestServer.RequestAsync(socket, token);
            Assert.AreEqual(0, await socket.ReceiveAsync(new byte[1], SocketFlags.None, token));
        });
        using var decoder = new UnixSocketImageDecoder(server.PathName, static _ => true);
        Assert.AreEqual(ImagePreviewFailure.SourceChanged, (await FailureAsync(decoder, new SocketImageSource { Changed = true })).Failure);
        await server.CompleteAsync();
    }

    [TestMethod]
    public async Task SourceBoundsRejectBeforeConnection()
    {
        using var decoder = new UnixSocketImageDecoder("unused.sock", static _ => throw new AssertFailedException("No peer call expected."));
        foreach (var length in new long[] { 0, -1, ImageWorkerProtocol.MaximumSourceBytes + 1L })
            Assert.AreEqual(length <= 0 ? ImagePreviewFailure.Invalid : ImagePreviewFailure.LimitExceeded,
                (await FailureAsync(decoder, new SocketImageSource { Length = length })).Failure);
    }

}

internal static class SocketImageAssertions
{
    public static Task<ImagePreviewException> FailureAsync(UnixSocketImageDecoder decoder, SocketImageSource source) =>
        Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await decoder.DecodeAsync(source, ImagePreviewVariant.Thumbnail, CancellationToken.None));
}
