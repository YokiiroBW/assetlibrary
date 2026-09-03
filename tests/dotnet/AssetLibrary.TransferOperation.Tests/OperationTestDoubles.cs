using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class MemoryOperationStore : IOperationPlanStore
{
    private readonly object sync = new();
    private readonly Dictionary<OperationIdempotencyKey, Entry> entries = [];

    public bool RefuseAcquire { get; set; }

    public bool RefuseCompletion { get; set; }

    public Func<OperationPlanResult, OperationCompletionResult>? CompletionOverride { get; set; }

    public ValueTask<OperationPrepareResult> SavePreparedAsync(
        PreparedOperationPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (entries.TryGetValue(plan.Request.IdempotencyKey, out var existing))
            {
                var status = SameSemantics(existing.Plan.Request, plan.Request)
                    ? OperationPrepareStatus.Existing
                    : OperationPrepareStatus.IdempotencyConflict;
                return ValueTask.FromResult(new OperationPrepareResult(status, existing.Plan));
            }

            entries.Add(plan.Request.IdempotencyKey, new Entry(plan, null, null));
            return ValueTask.FromResult(
                new OperationPrepareResult(OperationPrepareStatus.Created, plan));
        }
    }

    public ValueTask<OperationAcquireResult> TryAcquireAsync(
        OperationPlanId planId,
        OperationConfirmationDigest confirmation,
        OperationExecutionRight executionRight,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            var pair = entries.SingleOrDefault(
                candidate => candidate.Value.Plan.Request.PlanId == planId);
            if (pair.Value is null)
            {
                return ValueTask.FromResult(
                    new OperationAcquireResult(OperationAcquireStatus.NotFound));
            }

            if (pair.Value.Result is not null)
            {
                return ValueTask.FromResult(
                    new OperationAcquireResult(
                        OperationAcquireStatus.Existing,
                        pair.Value.Plan,
                        pair.Value.Result));
            }

            if (pair.Value.Plan.Preflight.Confirmation != confirmation)
            {
                return ValueTask.FromResult(
                    new OperationAcquireResult(
                        OperationAcquireStatus.ConfirmationMismatch,
                        pair.Value.Plan));
            }

            if (pair.Value.Plan.State != OperationPlanState.AwaitingConfirmation)
            {
                return ValueTask.FromResult(
                    new OperationAcquireResult(
                        OperationAcquireStatus.NotReady,
                        pair.Value.Plan));
            }

            if (RefuseAcquire
                || pair.Value.ExecutionRight is not null
                && pair.Value.ExecutionRight != executionRight)
            {
                return ValueTask.FromResult(
                    new OperationAcquireResult(
                        OperationAcquireStatus.NotCurrent,
                        pair.Value.Plan));
            }

            entries[pair.Key] = pair.Value with { ExecutionRight = executionRight };
            return ValueTask.FromResult(
                new OperationAcquireResult(OperationAcquireStatus.Acquired, pair.Value.Plan));
        }
    }

    public ValueTask<OperationCompletionResult> CompleteAsync(
        OperationPlanId planId,
        OperationExecutionRight executionRight,
        OperationPlanResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CompletionOverride is not null)
        {
            return ValueTask.FromResult(CompletionOverride(result));
        }

        lock (sync)
        {
            var pair = entries.Single(candidate => candidate.Value.Plan.Request.PlanId == planId);
            if (RefuseCompletion || pair.Value.ExecutionRight != executionRight)
            {
                return ValueTask.FromResult(
                    new OperationCompletionResult(OperationCompletionStatus.NotCurrent, null));
            }

            entries[pair.Key] = pair.Value with { Result = result };
            return ValueTask.FromResult(
                new OperationCompletionResult(OperationCompletionStatus.Accepted, result));
        }
    }

    private static bool SameSemantics(OperationPlanRequest left, OperationPlanRequest right) =>
        OperationPlanPolicy.HasSameIntent(left, right);

    private sealed record Entry(
        PreparedOperationPlan Plan,
        OperationExecutionRight? ExecutionRight,
        OperationPlanResult? Result);
}

internal sealed class StubPreflightPort : IOperationPreflightPort
{
    public Func<OperationPlanRequest, CancellationToken, ValueTask<IReadOnlyList<OperationItemPreflight>>>
        Handler
    { get; set; } = Ready;

    public int Calls { get; private set; }

    public static StubPreflightPort WithDecision(OperationPreflightDecision decision) =>
        new()
        {
            Handler = (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<OperationItemPreflight>>(
                    request.Items
                        .Select(item =>
                            new OperationItemPreflight(
                                item.ItemId,
                                decision,
                                item.ExpectedSource,
                                item.ExpectedSource.Length,
                                long.MaxValue))
                        .ToArray());
            },
        };

    public ValueTask<IReadOnlyList<OperationItemPreflight>> InspectAsync(
        OperationPlanRequest request,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Handler(request, cancellationToken);
    }

    private static ValueTask<IReadOnlyList<OperationItemPreflight>> Ready(
        OperationPlanRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<OperationItemPreflight>>(
            request.Items
                .Select(item =>
                    new OperationItemPreflight(
                        item.ItemId,
                        OperationPreflightDecision.Ready,
                        item.ExpectedSource,
                        item.ExpectedSource.Length,
                        item.ExpectedSource.Length * 2))
                .ToArray());
    }
}

internal sealed class StubOperationExecutor : IOperationItemExecutorPort
{
    public Func<OperationPlanRequest, OperationItemRequest, CancellationToken, ValueTask<OperationPortReceipt>>
        Handler
    { get; set; } = Completed;

    public int Calls { get; private set; }

    public static StubOperationExecutor Cancelling(CancellationTokenSource cancellation) =>
        new()
        {
            Handler = async (request, item, cancellationToken) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new OperationPortReceipt(
                    request.PlanId,
                    item.ItemId,
                    OperationPortOutcome.Completed,
                    item.ExpectedSource);
            },
        };

    public ValueTask<OperationPortReceipt> ExecuteAsync(
        OperationPlanRequest request,
        OperationItemRequest item,
        OperationItemPreflight preflight,
        OperationExecutionRight executionRight,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Handler(request, item, cancellationToken);
    }

    private static ValueTask<OperationPortReceipt> Completed(
        OperationPlanRequest request,
        OperationItemRequest item,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new OperationPortReceipt(
                request.PlanId,
                item.ItemId,
                OperationPortOutcome.Completed,
                item.ExpectedSource));
    }
}

internal sealed class CaptureOperationEvents : IOperationEventSink
{
    public List<OperationAuditEvent> Events { get; } = [];

    public void Record(OperationAuditEvent auditEvent) => Events.Add(auditEvent);
}
