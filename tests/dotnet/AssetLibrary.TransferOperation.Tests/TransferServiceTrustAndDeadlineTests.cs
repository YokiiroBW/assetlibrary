using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class TransferServiceStoreTrustTests
{
    [TestMethod]
    public async Task ExistingStoreResultWithDifferentIntentIsRejected()
    {
        var request = TransferTestData.Request("untrusted-existing");
        var different = request with
        {
            SessionId = TransferSessionId.New(),
            Target = new TransferLocationToken("different_target"),
        };
        var store = new MemoryTransferStore
        {
            StartOverride = new TransferStartResult(
                TransferStartStatus.Existing,
                different,
                new TransferResult(
                    different.SessionId,
                    TransferSessionState.Executing,
                    TransferFailureKind.None,
                    0,
                    null)),
        };
        var port = new StubTransferPort();
        var service = TransferTestData.Service(store, port);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.ExecuteAsync(
                request,
                TransferTestData.Right(),
                CancellationToken.None));
        Assert.AreEqual(0, port.Calls);
    }
}

[TestClass]
public sealed class TransferServiceDeadlineTests
{
    [TestMethod]
    public async Task RequestDeadlineProducesDistinctFailure()
    {
        var now = DateTimeOffset.UtcNow;
        var time = new MutableTimeProvider(now);
        var port = new StubTransferPort
        {
            Handler = (_, _) =>
            {
                time.Current = now.AddMinutes(2);
                throw new OperationCanceledException();
            },
        };
        var service = TransferTestData.Service(
            new MemoryTransferStore(),
            port,
            timeProvider: time);

        var result = await service.ExecuteAsync(
            TransferTestData.Request(deadline: now.AddMinutes(1)),
            TransferTestData.Right(now.AddMinutes(3)),
            CancellationToken.None);

        Assert.AreEqual(TransferSessionState.Cancelled, result.State);
        Assert.AreEqual(TransferFailureKind.DeadlineExceeded, result.Failure);
    }

    [TestMethod]
    public async Task TimeSpentStartingCannotExtendRequestDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        var time = new MutableTimeProvider(now);
        var store = new MemoryTransferStore
        {
            BeforeStart = () => time.Current = now.AddMinutes(2),
        };
        var port = new StubTransferPort();
        var service = TransferTestData.Service(store, port, timeProvider: time);

        var result = await service.ExecuteAsync(
            TransferTestData.Request(deadline: now.AddMinutes(1)),
            TransferTestData.Right(now.AddMinutes(3)),
            CancellationToken.None);

        Assert.AreEqual(TransferFailureKind.DeadlineExceeded, result.Failure);
        Assert.AreEqual(0, port.Calls);
    }
}
