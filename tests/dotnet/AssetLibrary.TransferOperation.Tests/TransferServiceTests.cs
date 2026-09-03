using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class TransferServiceTests
{
    [TestMethod]
    public async Task ExecuteCompletesOnlyWithExactReopenedFacts()
    {
        var store = new MemoryTransferStore();
        var events = new CaptureTransferEvents();
        var service = TransferTestData.Service(store, new StubTransferPort(), events);
        var request = TransferTestData.Request();

        var result = await service.ExecuteAsync(
            request,
            TransferTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(TransferSessionState.Completed, result.State);
        Assert.AreEqual(TransferFailureKind.None, result.Failure);
        Assert.AreEqual(request.ExpectedSource, result.VerifiedTarget);
        Assert.HasCount(1, events.Events);
    }

    [TestMethod]
    public async Task ExistingIdempotentResultDoesNotExecutePayloadTwice()
    {
        var store = new MemoryTransferStore();
        var port = new StubTransferPort();
        var service = TransferTestData.Service(store, port);
        var first = TransferTestData.Request("same-key");
        var right = TransferTestData.Right();
        var initial = await service.ExecuteAsync(first, right, CancellationToken.None);
        var replay = first with { SessionId = TransferSessionId.New() };

        var repeated = await service.ExecuteAsync(replay, right, CancellationToken.None);

        Assert.AreEqual(initial, repeated);
        Assert.AreEqual(1, port.Calls);
    }

    [TestMethod]
    public async Task ReusedIdempotencyKeyWithDifferentTargetConflicts()
    {
        var store = new MemoryTransferStore();
        var port = new StubTransferPort();
        var service = TransferTestData.Service(store, port);
        var first = TransferTestData.Request("collision-key");
        await service.ExecuteAsync(first, TransferTestData.Right(), CancellationToken.None);
        var conflicting = first with
        {
            SessionId = TransferSessionId.New(),
            Target = new TransferLocationToken("other_target"),
        };

        var result = await service.ExecuteAsync(
            conflicting,
            TransferTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(TransferSessionState.Conflict, result.State);
        Assert.AreEqual(TransferFailureKind.IdempotencyConflict, result.Failure);
        Assert.AreEqual(1, port.Calls);
    }

    [TestMethod]
    public async Task ExpiredExecutionRightFailsBeforePort()
    {
        var port = new StubTransferPort();
        var service = TransferTestData.Service(new MemoryTransferStore(), port);

        var result = await service.ExecuteAsync(
            TransferTestData.Request(),
            TransferTestData.Right(DateTimeOffset.UtcNow.AddMinutes(-1)),
            CancellationToken.None);

        Assert.AreEqual(TransferSessionState.ManualReview, result.State);
        Assert.AreEqual(TransferFailureKind.StaleExecutionRight, result.Failure);
        Assert.AreEqual(0, port.Calls);
    }

    [TestMethod]
    public async Task ServiceTimeoutReturnsDistinctDeadlineFailure()
    {
        var port = new StubTransferPort
        {
            Handler = static async (request, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new TransferPortReceipt(
                    request.SessionId,
                    TransferPortOutcome.Completed,
                    request.ExpectedSource.Length,
                    request.ExpectedSource);
            },
        };
        var service = TransferTestData.Service(
            new MemoryTransferStore(),
            port,
            timeout: TimeSpan.FromMilliseconds(25));

        var result = await service.ExecuteAsync(
            TransferTestData.Request(),
            TransferTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(TransferSessionState.Cancelled, result.State);
        Assert.AreEqual(TransferFailureKind.DeadlineExceeded, result.Failure);
    }

    [TestMethod]
    public async Task UntrustedCompletedReceiptWithWrongFactsIsRejected()
    {
        var port = new StubTransferPort
        {
            Handler = static (request, _) =>
                ValueTask.FromResult(
                    new TransferPortReceipt(
                        request.SessionId,
                        TransferPortOutcome.Completed,
                        request.ExpectedSource.Length,
                        PayloadTestData.Facts("wrong"u8.ToArray()))),
        };
        var service = TransferTestData.Service(new MemoryTransferStore(), port);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.ExecuteAsync(
                TransferTestData.Request(),
                TransferTestData.Right(),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task StaleCompletionCannotClaimSuccess()
    {
        var store = new MemoryTransferStore { RefuseCompletion = true };
        var service = TransferTestData.Service(store, new StubTransferPort());

        var result = await service.ExecuteAsync(
            TransferTestData.Request(),
            TransferTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(TransferSessionState.ManualReview, result.State);
        Assert.AreEqual(TransferFailureKind.StaleExecutionRight, result.Failure);
    }

}
