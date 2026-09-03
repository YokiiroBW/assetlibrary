using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;

namespace AssetLibrary.Modules.OperationTrash.Application;

internal sealed class OperationPlanPreparation(
    IOperationPlanStore planStore,
    IOperationPreflightPort preflightPort,
    IOperationEventSink eventSink,
    TimeProvider timeProvider,
    OperationExecutionLimits limits)
{
    public async ValueTask<PreparedOperationPlan> PrepareAsync(
        OperationPlanRequest request,
        CancellationToken cancellationToken)
    {
        OperationPlanPolicy.ValidateRequest(request);
        if (request.Items.Count > limits.MaximumItems)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"This service accepts at most {limits.MaximumItems} items.");
        }

        var now = timeProvider.GetUtcNow();
        if (request.Deadline.ToUniversalTime() <= now)
        {
            throw new TimeoutException("The operation deadline has elapsed.");
        }

        var timeout = OperationDeadline.Shortest(
            limits.OperationTimeout,
            request.Deadline.ToUniversalTime() - now);
        IReadOnlyList<OperationItemPreflight> inspected;
        using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            bounded.CancelAfter(timeout);
            inspected = await preflightPort.InspectAsync(request, bounded.Token).ConfigureAwait(false);
        }

        OperationPreflightValidator.Validate(request, inspected);
        var inspectedAt = timeProvider.GetUtcNow();
        if (request.Deadline.ToUniversalTime() <= inspectedAt)
        {
            throw new TimeoutException("The operation deadline elapsed during preflight.");
        }

        var expiresAt = OperationDeadline.Earliest(
            request.Deadline.ToUniversalTime(),
            inspectedAt + limits.PreflightLifetime);
        var snapshot = new OperationPreflightSnapshot(
            request.PlanId,
            inspected,
            OperationConfirmation.Create(request, inspected, expiresAt),
            expiresAt);
        var candidate = new PreparedOperationPlan(
            request,
            OperationPlanPolicy.PreparedState(inspected),
            snapshot);
        OperationPrepareResult saved;
        using (var storeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var saveStartedAt = timeProvider.GetUtcNow();
            if (expiresAt <= saveStartedAt)
            {
                throw new TimeoutException("The operation preflight expired before it was saved.");
            }

            storeTimeout.CancelAfter(
                OperationDeadline.Shortest(
                    limits.OperationTimeout,
                    expiresAt - saveStartedAt));
            saved = await planStore.SavePreparedAsync(
                candidate,
                saveStartedAt,
                storeTimeout.Token).ConfigureAwait(false);
        }

        if (saved.Status == OperationPrepareStatus.IdempotencyConflict)
        {
            throw new InvalidOperationException(
                "The operation idempotency key is already bound to different semantics.");
        }

        if (saved.Status is not OperationPrepareStatus.Created
            and not OperationPrepareStatus.Existing)
        {
            throw new InvalidOperationException("The operation store returned an invalid prepared plan.");
        }

        ValidateStoredPlan(saved, candidate);

        eventSink.Record(
            new OperationAuditEvent(
                saved.Plan.Request.PlanId,
                saved.Plan.State,
                0,
                saved.Plan.Request.Items.Count));
        return saved.Plan;
    }

    private static void ValidateStoredPlan(
        OperationPrepareResult saved,
        PreparedOperationPlan candidate)
    {
        var plan = saved.Plan
            ?? throw new InvalidOperationException(
                "The operation store omitted the prepared plan.");
        OperationPlanPolicy.ValidateRequest(plan.Request);
        OperationPreflightValidator.Validate(plan.Request, plan.Preflight.Items);
        var expectedState = OperationPlanPolicy.PreparedState(plan.Preflight.Items);
        var expectedConfirmation = OperationConfirmation.Create(
            plan.Request,
            plan.Preflight.Items,
            plan.Preflight.ExpiresAt);
        var invalidCreatedSnapshot = saved.Status == OperationPrepareStatus.Created
            && (plan.Request.PlanId != candidate.Request.PlanId
                || plan.State != candidate.State
                || plan.Preflight.PlanId != candidate.Preflight.PlanId
                || plan.Preflight.ExpiresAt != candidate.Preflight.ExpiresAt
                || plan.Preflight.Confirmation != candidate.Preflight.Confirmation
                || !plan.Request.Items.Select(item => item.ItemId)
                    .SequenceEqual(candidate.Request.Items.Select(item => item.ItemId))
                || !plan.Preflight.Items.SequenceEqual(candidate.Preflight.Items));
        if (!OperationPlanPolicy.HasSameIntent(plan.Request, candidate.Request)
            || plan.Preflight.PlanId != plan.Request.PlanId
            || plan.State != expectedState
            || plan.Preflight.Confirmation != expectedConfirmation
            || invalidCreatedSnapshot)
        {
            throw new InvalidOperationException(
                "The operation store returned inconsistent prepared state.");
        }
    }
}

internal static class OperationPreflightValidator
{
    public static void Validate(
        OperationPlanRequest request,
        IReadOnlyList<OperationItemPreflight> inspected)
    {
        ArgumentNullException.ThrowIfNull(inspected);
        if (inspected.Count != request.Items.Count
            || inspected.Select(item => item.ItemId).Distinct().Count() != inspected.Count)
        {
            throw new InvalidOperationException(
                "The preflight adapter must return every item exactly once.");
        }

        foreach (var item in request.Items)
        {
            var result = inspected.SingleOrDefault(candidate => candidate.ItemId == item.ItemId)
                ?? throw new InvalidOperationException("The preflight adapter omitted an item.");
            ArgumentOutOfRangeException.ThrowIfNegative(result.RequiredBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(result.AvailableBytes);
            if (!Enum.IsDefined(result.Decision))
            {
                throw new InvalidOperationException(
                    "The preflight adapter returned an unknown decision.");
            }

            var expectedRequiredBytes = request.Operation is PhysicalOperationKind.Copy
                or PhysicalOperationKind.Move
                or PhysicalOperationKind.Restore
                ? item.ExpectedSource.Length
                : 0;
            if (result.Decision == OperationPreflightDecision.Ready
                && (result.ObservedSource != item.ExpectedSource
                    || result.RequiredBytes != expectedRequiredBytes
                    || result.AvailableBytes < result.RequiredBytes))
            {
                throw new InvalidOperationException(
                    "A ready preflight must bind exact source and capacity facts.");
            }
        }
    }
}
