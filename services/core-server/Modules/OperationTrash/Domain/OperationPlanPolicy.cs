using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Domain;

public static class OperationPlanPolicy
{
    public const int MaximumItems = 128;

    public static void ValidateExecution(
        OperationPlanId planId,
        OperationConfirmationDigest confirmation,
        OperationExecutionRight executionRight)
    {
        if (planId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(confirmation.Value)
            || string.IsNullOrWhiteSpace(executionRight.Worker.Value)
            || executionRight.Token.Value == Guid.Empty
            || executionRight.Generation <= 0
            || executionRight.ExpiresAt == default)
        {
            throw new ArgumentException("The operation execution contains a default value object.");
        }
    }

    public static void ValidateRequest(OperationPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Items);
        if (request.PlanId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(request.IdempotencyKey.Value))
        {
            throw new ArgumentException(
                "The operation plan contains a default value object.",
                nameof(request));
        }

        if (!Enum.IsDefined(request.Operation))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.Items.Count is < 1 or > MaximumItems)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"An operation plan must contain between 1 and {MaximumItems} items.");
        }

        if (request.Deadline == default)
        {
            throw new ArgumentException("An operation deadline is required.", nameof(request));
        }

        var identifiers = new HashSet<OperationItemId>();
        foreach (var item in request.Items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.ExpectedSource);
            if (item.ItemId.Value == Guid.Empty
                || string.IsNullOrWhiteSpace(item.Source.Value)
                || item.Target.HasValue
                    && string.IsNullOrWhiteSpace(item.Target.Value.Value)
                || string.IsNullOrWhiteSpace(item.ExpectedSource.Sha256.Value))
            {
                throw new ArgumentException(
                    "An operation item contains a default value object.",
                    nameof(request));
            }

            if (!identifiers.Add(item.ItemId))
            {
                throw new ArgumentException("Operation item identifiers must be unique.", nameof(request));
            }

            ValidateTarget(request.Operation, item);
        }
    }

    public static OperationPlanState PreparedState(
        IReadOnlyList<OperationItemPreflight> preflight) =>
        preflight.All(item => item.Decision == OperationPreflightDecision.Ready)
            ? OperationPlanState.AwaitingConfirmation
            : OperationPlanState.Rejected;

    public static bool HasSameIntent(OperationPlanRequest left, OperationPlanRequest right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.IdempotencyKey == right.IdempotencyKey
            && left.Operation == right.Operation
            && left.Deadline.ToUniversalTime() == right.Deadline.ToUniversalTime()
            && left.Items.Count == right.Items.Count
            && left.Items.Zip(right.Items).All(pair =>
                pair.First.Source == pair.Second.Source
                && pair.First.Target == pair.Second.Target
                && pair.First.ExpectedSource == pair.Second.ExpectedSource);
    }

    public static bool IsTerminal(OperationPlanState state) =>
        state is OperationPlanState.Rejected
            or OperationPlanState.PartiallyCompleted
            or OperationPlanState.Completed
            or OperationPlanState.Failed
            or OperationPlanState.Cancelled
            or OperationPlanState.ManualReview;

    public static bool CanTransition(OperationPlanState current, OperationPlanState next) =>
        current switch
        {
            OperationPlanState.AwaitingConfirmation => next is OperationPlanState.Executing
                or OperationPlanState.Cancelled
                or OperationPlanState.ManualReview,
            OperationPlanState.Executing => next is OperationPlanState.PartiallyCompleted
                or OperationPlanState.Completed
                or OperationPlanState.Failed
                or OperationPlanState.Cancelled
                or OperationPlanState.ManualReview,
            _ => false,
        };

    public static bool HasSameResult(OperationPlanResult left, OperationPlanResult right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.PlanId == right.PlanId
            && left.State == right.State
            && left.Items.SequenceEqual(right.Items);
    }

    public static void ValidateResult(
        PreparedOperationPlan plan,
        OperationPlanResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        if (result.PlanId != plan.Request.PlanId
            || !IsTerminal(result.State)
            || result.Items.Count != plan.Request.Items.Count
            || result.Items.Select(item => item.ItemId).Distinct().Count()
                != result.Items.Count)
        {
            throw new InvalidOperationException("The operation result is structurally invalid.");
        }

        foreach (var requestItem in plan.Request.Items)
        {
            var item = result.Items.SingleOrDefault(
                candidate => candidate.ItemId == requestItem.ItemId)
                ?? throw new InvalidOperationException("The operation result omitted an item.");
            if (!Enum.IsDefined(item.Failure))
            {
                throw new InvalidOperationException(
                    "The operation result contains an unknown failure kind.");
            }

            var validSuccess = item.Succeeded
                && item.Failure == OperationItemFailure.None
                && item.VerifiedDestination == requestItem.ExpectedSource;
            var validFailure = !item.Succeeded
                && item.Failure != OperationItemFailure.None
                && item.VerifiedDestination is null;
            if (!validSuccess && !validFailure)
            {
                throw new InvalidOperationException(
                    "The operation item result is internally inconsistent.");
            }
        }

        if (Aggregate(result.Items) != result.State)
        {
            throw new InvalidOperationException(
                "The operation aggregate state does not match its item results.");
        }
    }

    public static OperationPlanState Aggregate(IReadOnlyList<OperationItemResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            throw new ArgumentException("An operation result cannot be empty.", nameof(results));
        }

        var succeeded = results.Count(result => result.Succeeded);
        if (succeeded == results.Count)
        {
            return OperationPlanState.Completed;
        }

        if (succeeded > 0)
        {
            return OperationPlanState.PartiallyCompleted;
        }

        if (results.All(result =>
            result.Failure is OperationItemFailure.Cancelled
                or OperationItemFailure.DeadlineExceeded))
        {
            return OperationPlanState.Cancelled;
        }

        return results.Any(result =>
            result.Failure is OperationItemFailure.ManualReviewRequired
                or OperationItemFailure.StaleExecutionRight)
            ? OperationPlanState.ManualReview
            : OperationPlanState.Failed;
    }

    public static OperationItemResult FromReceipt(
        OperationPlanRequest request,
        OperationItemRequest item,
        OperationPortReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.PlanId != request.PlanId || receipt.ItemId != item.ItemId)
        {
            throw new InvalidOperationException("The operation adapter returned another item.");
        }

        if (receipt.Outcome == OperationPortOutcome.Completed)
        {
            if (receipt.VerifiedDestination != item.ExpectedSource)
            {
                throw new InvalidOperationException(
                    "A completed operation must return the exact reopened destination facts.");
            }

            return new(item.ItemId, true, OperationItemFailure.None, receipt.VerifiedDestination);
        }

        if (receipt.VerifiedDestination is not null)
        {
            throw new InvalidOperationException(
                "A failed operation cannot claim a verified destination.");
        }

        return new(item.ItemId, false, MapFailure(receipt.Outcome), null);
    }

    public static OperationItemResult Cancelled(
        OperationItemId itemId,
        bool deadlineExceeded) =>
        new(
            itemId,
            false,
            deadlineExceeded
                ? OperationItemFailure.DeadlineExceeded
                : OperationItemFailure.Cancelled,
            null);

    public static OperationItemResult Stale(OperationItemId itemId) =>
        new(itemId, false, OperationItemFailure.StaleExecutionRight, null);

    private static void ValidateTarget(
        PhysicalOperationKind operation,
        OperationItemRequest item)
    {
        var targetRequired = operation is not PhysicalOperationKind.Trash;
        if (targetRequired != item.Target.HasValue)
        {
            throw new ArgumentException(
                targetRequired
                    ? "This operation requires a target token."
                    : "A trash operation cannot provide a target token.",
                nameof(item));
        }

        if (item.Target == item.Source)
        {
            throw new ArgumentException("Source and target tokens must differ.", nameof(item));
        }
    }

    private static OperationItemFailure MapFailure(OperationPortOutcome outcome) => outcome switch
    {
        OperationPortOutcome.PermissionDenied => OperationItemFailure.PermissionDenied,
        OperationPortOutcome.Protected => OperationItemFailure.Protected,
        OperationPortOutcome.SourceMissing => OperationItemFailure.SourceMissing,
        OperationPortOutcome.SourceChanged => OperationItemFailure.SourceChanged,
        OperationPortOutcome.TargetExists => OperationItemFailure.TargetExists,
        OperationPortOutcome.HashMismatch => OperationItemFailure.HashMismatch,
        OperationPortOutcome.InsufficientSpace => OperationItemFailure.InsufficientSpace,
        OperationPortOutcome.UnsafeSandbox => OperationItemFailure.UnsafeSandbox,
        OperationPortOutcome.ManualReviewRequired => OperationItemFailure.ManualReviewRequired,
        _ => throw new InvalidOperationException("The operation adapter returned an unknown outcome."),
    };
}
