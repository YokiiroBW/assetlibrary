using AssetLibrary.Modules.TransferSync.Application;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class MemoryTransferStore : ITransferSessionStore
{
    private readonly object sync = new();
    private readonly Dictionary<TransferIdempotencyKey, Entry> entries = [];

    public bool RefuseStart { get; set; }

    public bool RefuseCompletion { get; set; }

    public TransferStartResult? StartOverride { get; set; }

    public Action? BeforeStart { get; set; }

    public ValueTask<TransferStartResult> TryStartAsync(
        TransferRequest request,
        TransferExecutionRight executionRight,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeStart?.Invoke();
        if (StartOverride is not null)
        {
            return ValueTask.FromResult(StartOverride);
        }

        lock (sync)
        {
            if (RefuseStart)
            {
                return ValueTask.FromResult(
                    new TransferStartResult(TransferStartStatus.NotCurrent));
            }

            if (entries.TryGetValue(request.IdempotencyKey, out var existing))
            {
                var same = SameSemantics(existing.Request, request);
                return ValueTask.FromResult(
                    same
                        ? new TransferStartResult(
                            TransferStartStatus.Existing,
                            existing.Request,
                            existing.Result
                            ?? new TransferResult(
                                existing.Request.SessionId,
                                TransferSessionState.Executing,
                                TransferFailureKind.None,
                                0,
                                null))
                        : new TransferStartResult(TransferStartStatus.IdempotencyConflict));
            }

            entries.Add(request.IdempotencyKey, new Entry(request, executionRight, null));
            return ValueTask.FromResult(new TransferStartResult(TransferStartStatus.Started));
        }
    }

    public ValueTask<TransferCompletionResult> CompleteAsync(
        TransferSessionId sessionId,
        TransferExecutionRight executionRight,
        TransferResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            var pair = entries.Single(candidate => candidate.Value.Request.SessionId == sessionId);
            if (RefuseCompletion || pair.Value.ExecutionRight != executionRight)
            {
                return ValueTask.FromResult(
                    new TransferCompletionResult(TransferCompletionStatus.NotCurrent, null));
            }

            entries[pair.Key] = pair.Value with { Result = result };
            return ValueTask.FromResult(
                new TransferCompletionResult(TransferCompletionStatus.Accepted, result));
        }
    }

    private static bool SameSemantics(TransferRequest left, TransferRequest right) =>
        AssetLibrary.Modules.TransferSync.Domain.TransferStatePolicy.HasSameIntent(left, right);

    private sealed record Entry(
        TransferRequest Request,
        TransferExecutionRight ExecutionRight,
        TransferResult? Result);
}

internal sealed class StubTransferPort : ITransferPayloadPort
{
    public Func<TransferRequest, CancellationToken, ValueTask<TransferPortReceipt>> Handler
    {
        get;
        set;
    } = static (request, _) =>
        ValueTask.FromResult(
            new TransferPortReceipt(
                request.SessionId,
                TransferPortOutcome.Completed,
                request.ExpectedSource.Length,
                request.ExpectedSource));

    public int Calls { get; private set; }

    public ValueTask<TransferPortReceipt> TransferAsync(
        TransferRequest request,
        TransferExecutionRight executionRight,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Handler(request, cancellationToken);
    }
}

internal sealed class CaptureTransferEvents : ITransferEventSink
{
    public List<TransferAuditEvent> Events { get; } = [];

    public void Record(TransferAuditEvent auditEvent) => Events.Add(auditEvent);
}
