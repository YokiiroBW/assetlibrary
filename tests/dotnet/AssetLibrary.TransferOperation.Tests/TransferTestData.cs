using AssetLibrary.Modules.TransferSync.Application;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal static class TransferTestData
{
    public static TransferRequest Request(
        string idempotencyKey = "transfer-test",
        PayloadFacts? facts = null,
        DateTimeOffset? deadline = null) =>
        new(
            TransferSessionId.New(),
            new TransferIdempotencyKey(idempotencyKey),
            new TransferLocationToken("source_token"),
            new TransferLocationToken("target_token"),
            facts ?? PayloadTestData.Facts(),
            deadline ?? DateTimeOffset.UtcNow.AddMinutes(1));

    public static TransferExecutionRight Right(DateTimeOffset? expiresAt = null) =>
        new(
            new TransferWorker("worker_1"),
            TransferLeaseToken.New(),
            1,
            expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(1));

    public static TransferService Service(
        MemoryTransferStore store,
        ITransferPayloadPort port,
        CaptureTransferEvents? events = null,
        TimeProvider? timeProvider = null,
        TimeSpan? timeout = null) =>
        new(
            store,
            port,
            events ?? new(),
            timeProvider ?? TimeProvider.System,
            new TransferExecutionLimits(timeout ?? TimeSpan.FromSeconds(2)));
}
