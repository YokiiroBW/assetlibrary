using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;

namespace AssetLibrary.Modules.OperationTrash.Application;

internal sealed class OperationPlanExecution(
    IOperationPlanStore planStore,
    IOperationItemExecutorPort executorPort,
    IOperationEventSink eventSink,
    TimeProvider timeProvider,
    OperationExecutionLimits limits)
{
    public async ValueTask<OperationPlanResult> ExecuteAsync(
        OperationPlanId planId,
        OperationConfirmationDigest confirmation,
        OperationExecutionRight executionRight,
        CancellationToken cancellationToken)
    {
        OperationPlanPolicy.ValidateExecution(planId, confirmation, executionRight);
        var now = timeProvider.GetUtcNow();
        OperationAcquireResult acquired;
        using (var acquireTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            acquireTimeout.CancelAfter(limits.OperationTimeout);
            acquired = await planStore.TryAcquireAsync(
                planId,
                confirmation,
                executionRight,
                now,
                acquireTimeout.Token).ConfigureAwait(false);
        }

        if (acquired.Status == OperationAcquireStatus.Existing)
        {
            return ValidateExisting(planId, confirmation, acquired);
        }

        if (acquired.Status == OperationAcquireStatus.NotCurrent)
        {
            return Stale(planId, acquired.Plan);
        }

        if (acquired.Status is OperationAcquireStatus.NotFound
            or OperationAcquireStatus.ConfirmationMismatch
            or OperationAcquireStatus.NotReady)
        {
            throw new InvalidOperationException(
                $"The operation cannot execute because acquisition returned {acquired.Status}.");
        }

        var plan = ValidateAcquired(planId, acquired, confirmation);
        if (plan.Preflight.ExpiresAt <= now
            || plan.Request.Deadline.ToUniversalTime() <= now
            || executionRight.ExpiresAt <= now)
        {
            return Stale(planId, plan);
        }

        var results = await ExecuteItemsAsync(
            plan,
            executionRight,
            cancellationToken).ConfigureAwait(false);
        var candidate = new OperationPlanResult(
            planId,
            OperationPlanPolicy.Aggregate(results),
            results);
        if (!OperationPlanPolicy.CanTransition(
                OperationPlanState.Executing,
                candidate.State))
        {
            throw new InvalidOperationException("The operation result regressed its state.");
        }

        OperationPlanPolicy.ValidateResult(plan, candidate);
        OperationCompletionResult completion;
        using (var completionTimeout = new CancellationTokenSource(limits.OperationTimeout))
        {
            completion = await planStore.CompleteAsync(
                planId,
                executionRight,
                candidate,
                timeProvider.GetUtcNow(),
                completionTimeout.Token).ConfigureAwait(false);
        }

        if (completion.Status == OperationCompletionStatus.NotCurrent)
        {
            return Stale(planId, plan);
        }

        if (completion.Status != OperationCompletionStatus.Accepted
            || completion.Result is null
            || !OperationPlanPolicy.HasSameResult(completion.Result, candidate))
        {
            throw new InvalidOperationException("The operation store returned an invalid completion.");
        }

        Record(candidate);
        return candidate;
    }

    private async ValueTask<IReadOnlyList<OperationItemResult>> ExecuteItemsAsync(
        PreparedOperationPlan plan,
        OperationExecutionRight executionRight,
        CancellationToken cancellationToken)
    {
        var results = new List<OperationItemResult>(plan.Request.Items.Count);
        for (var index = 0; index < plan.Request.Items.Count; index++)
        {
            var item = plan.Request.Items[index];
            var inspected = plan.Preflight.Items.Single(candidate => candidate.ItemId == item.ItemId);
            var now = timeProvider.GetUtcNow();
            var absoluteDeadline = OperationDeadline.Earliest(
                plan.Request.Deadline.ToUniversalTime(),
                plan.Preflight.ExpiresAt,
                executionRight.ExpiresAt);
            if (absoluteDeadline <= now || cancellationToken.IsCancellationRequested)
            {
                AddTerminatedRemainder(
                    plan,
                    results,
                    index,
                    executionRight,
                    now,
                    cancellationToken);
                break;
            }

            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(
                OperationDeadline.Shortest(
                    limits.OperationTimeout,
                    absoluteDeadline - now));
            try
            {
                var receipt = await executorPort.ExecuteAsync(
                    plan.Request,
                    item,
                    inspected,
                    executionRight,
                    bounded.Token).ConfigureAwait(false);
                results.Add(OperationPlanPolicy.FromReceipt(plan.Request, item, receipt));
            }
            catch (OperationCanceledException)
            {
                var cancelledAt = timeProvider.GetUtcNow();
                if (!cancellationToken.IsCancellationRequested
                    && !bounded.IsCancellationRequested
                    && absoluteDeadline > cancelledAt)
                {
                    throw;
                }

                AddTerminatedRemainder(
                    plan,
                    results,
                    index,
                    executionRight,
                    cancelledAt,
                    cancellationToken);
                break;
            }
        }

        return results;
    }

    private static PreparedOperationPlan ValidateAcquired(
        OperationPlanId planId,
        OperationAcquireResult acquired,
        OperationConfirmationDigest confirmation)
    {
        var plan = acquired.Status == OperationAcquireStatus.Acquired
            ? acquired.Plan
            : null;
        if (plan is null
            || plan.Request.PlanId != planId
            || plan.State != OperationPlanState.AwaitingConfirmation
            || plan.Preflight.PlanId != planId
            || plan.Preflight.Confirmation != confirmation
            || OperationConfirmation.Create(
                plan.Request,
                plan.Preflight.Items,
                plan.Preflight.ExpiresAt) != confirmation)
        {
            throw new InvalidOperationException("The operation store returned an invalid acquisition.");
        }

        OperationPlanPolicy.ValidateRequest(plan.Request);
        OperationPreflightValidator.Validate(plan.Request, plan.Preflight.Items);

        return plan;
    }

    private static OperationPlanResult ValidateExisting(
        OperationPlanId planId,
        OperationConfirmationDigest confirmation,
        OperationAcquireResult acquired)
    {
        var plan = acquired.Plan
            ?? throw new InvalidOperationException(
                "An existing operation must include its prepared plan.");
        var result = acquired.ExistingResult
            ?? throw new InvalidOperationException(
                "An existing operation must include its result.");
        if (plan.Request.PlanId != planId
            || result.PlanId != planId
            || plan.State != OperationPlanState.AwaitingConfirmation
            || plan.Preflight.PlanId != planId
            || plan.Preflight.Confirmation != confirmation)
        {
            throw new InvalidOperationException(
                "The operation store returned an invalid existing result.");
        }

        OperationPlanPolicy.ValidateRequest(plan.Request);
        OperationPreflightValidator.Validate(plan.Request, plan.Preflight.Items);
        if (OperationConfirmation.Create(
                plan.Request,
                plan.Preflight.Items,
                plan.Preflight.ExpiresAt) != confirmation)
        {
            throw new InvalidOperationException(
                "The existing operation is not bound to the supplied confirmation.");
        }

        OperationPlanPolicy.ValidateResult(plan, result);
        return result;
    }

    private static void AddTerminatedRemainder(
        PreparedOperationPlan plan,
        List<OperationItemResult> results,
        int firstIndex,
        OperationExecutionRight executionRight,
        DateTimeOffset now,
        CancellationToken callerCancellation)
    {
        var callerCancelled = callerCancellation.IsCancellationRequested;
        var executionRightExpired = !callerCancelled && executionRight.ExpiresAt <= now;
        for (var index = firstIndex; index < plan.Request.Items.Count; index++)
        {
            results.Add(
                executionRightExpired
                    ? OperationPlanPolicy.Stale(plan.Request.Items[index].ItemId)
                    : OperationPlanPolicy.Cancelled(
                        plan.Request.Items[index].ItemId,
                        deadlineExceeded: !callerCancelled));
        }
    }

    private OperationPlanResult Stale(
        OperationPlanId planId,
        PreparedOperationPlan? plan)
    {
        var items = plan?.Request.Items
            .Select(item => OperationPlanPolicy.Stale(item.ItemId))
            .ToArray()
            ?? [];
        var result = new OperationPlanResult(planId, OperationPlanState.ManualReview, items);
        Record(result);
        return result;
    }

    private void Record(OperationPlanResult result) =>
        eventSink.Record(
            new OperationAuditEvent(
                result.PlanId,
                result.State,
                result.Items.Count(item => item.Succeeded),
                result.Items.Count(item => !item.Succeeded)));
}
