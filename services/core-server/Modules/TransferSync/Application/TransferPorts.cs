using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.TransferSync.Application;

public interface ITransferSessionStore
{
    ValueTask<TransferStartResult> TryStartAsync(
        TransferRequest request,
        TransferExecutionRight executionRight,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<TransferCompletionResult> CompleteAsync(
        TransferSessionId sessionId,
        TransferExecutionRight executionRight,
        TransferResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface ITransferPayloadPort
{
    ValueTask<TransferPortReceipt> TransferAsync(
        TransferRequest request,
        TransferExecutionRight executionRight,
        CancellationToken cancellationToken);
}

public interface ITransferEventSink
{
    void Record(TransferAuditEvent auditEvent);
}
