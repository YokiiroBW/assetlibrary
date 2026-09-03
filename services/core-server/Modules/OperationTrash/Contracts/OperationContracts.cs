using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Contracts;

public readonly record struct OperationPlanId
{
    public OperationPlanId(Guid value)
    {
        Value = OperationContractRules.NonEmpty(value, nameof(value));
    }

    public Guid Value { get; }

    public static OperationPlanId New() => new(Guid.NewGuid());
}

public readonly record struct OperationItemId
{
    public OperationItemId(Guid value)
    {
        Value = OperationContractRules.NonEmpty(value, nameof(value));
    }

    public Guid Value { get; }

    public static OperationItemId New() => new(Guid.NewGuid());
}

public readonly record struct OperationLeaseToken
{
    public OperationLeaseToken(Guid value)
    {
        Value = OperationContractRules.NonEmpty(value, nameof(value));
    }

    public Guid Value { get; }

    public static OperationLeaseToken New() => new(Guid.NewGuid());
}

public readonly record struct OperationIdempotencyKey
{
    public OperationIdempotencyKey(string value)
    {
        Value = OperationContractRules.Required(value, 200, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct OperationWorker
{
    public OperationWorker(string value)
    {
        var candidate = OperationContractRules.Required(value, 120, nameof(value));
        if (!char.IsAsciiLetterLower(candidate[0])
            || candidate.Any(character =>
                !char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character)
                && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "An operation worker must be a lowercase ASCII identifier.",
                nameof(value));
        }

        Value = candidate;
    }

    public string Value { get; }
}

public readonly record struct OperationExecutionRight
{
    public OperationExecutionRight(
        OperationWorker worker,
        OperationLeaseToken token,
        long generation,
        DateTimeOffset expiresAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        if (expiresAt == default)
        {
            throw new ArgumentException("An execution right expiry is required.", nameof(expiresAt));
        }

        Worker = new OperationWorker(worker.Value);
        Token = new OperationLeaseToken(token.Value);
        Generation = generation;
        ExpiresAt = expiresAt.ToUniversalTime();
    }

    public OperationWorker Worker { get; }

    public OperationLeaseToken Token { get; }

    public long Generation { get; }

    public DateTimeOffset ExpiresAt { get; }
}

public readonly record struct OperationConfirmationDigest
{
    public OperationConfirmationDigest(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != Sha256Digest.HexLength
            || value.Any(character =>
                !char.IsAsciiDigit(character) && character is < 'a' or > 'f'))
        {
            throw new ArgumentException(
                "An operation confirmation digest must be lowercase SHA-256.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public enum PhysicalOperationKind
{
    Copy = 0,
    Move = 1,
    Rename = 2,
    Trash = 3,
    Restore = 4,
}

public enum OperationPlanState
{
    AwaitingConfirmation = 0,
    Rejected = 1,
    Executing = 2,
    PartiallyCompleted = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
    ManualReview = 7,
}

public enum OperationPreflightDecision
{
    Ready = 0,
    PermissionDenied = 1,
    Protected = 2,
    SourceMissing = 3,
    SourceChanged = 4,
    TargetExists = 5,
    InsufficientSpace = 6,
    UnsafeSandbox = 7,
}

public enum OperationItemFailure
{
    None = 0,
    PermissionDenied = 1,
    Protected = 2,
    SourceMissing = 3,
    SourceChanged = 4,
    TargetExists = 5,
    HashMismatch = 6,
    InsufficientSpace = 7,
    Cancelled = 8,
    DeadlineExceeded = 9,
    StaleExecutionRight = 10,
    UnsafeSandbox = 11,
    ManualReviewRequired = 12,
}

public sealed record OperationItemRequest(
    OperationItemId ItemId,
    TransferLocationToken Source,
    TransferLocationToken? Target,
    PayloadFacts ExpectedSource);

public sealed record OperationPlanRequest(
    OperationPlanId PlanId,
    OperationIdempotencyKey IdempotencyKey,
    PhysicalOperationKind Operation,
    IReadOnlyList<OperationItemRequest> Items,
    DateTimeOffset Deadline);

public sealed record OperationItemPreflight(
    OperationItemId ItemId,
    OperationPreflightDecision Decision,
    PayloadFacts? ObservedSource,
    long RequiredBytes,
    long AvailableBytes);

public sealed record OperationPreflightSnapshot(
    OperationPlanId PlanId,
    IReadOnlyList<OperationItemPreflight> Items,
    OperationConfirmationDigest Confirmation,
    DateTimeOffset ExpiresAt);

public sealed record PreparedOperationPlan(
    OperationPlanRequest Request,
    OperationPlanState State,
    OperationPreflightSnapshot Preflight);

public enum OperationPortOutcome
{
    Completed = 0,
    PermissionDenied = 1,
    Protected = 2,
    SourceMissing = 3,
    SourceChanged = 4,
    TargetExists = 5,
    HashMismatch = 6,
    InsufficientSpace = 7,
    UnsafeSandbox = 8,
    ManualReviewRequired = 9,
}

public sealed record OperationPortReceipt(
    OperationPlanId PlanId,
    OperationItemId ItemId,
    OperationPortOutcome Outcome,
    PayloadFacts? VerifiedDestination);

public sealed record OperationItemResult(
    OperationItemId ItemId,
    bool Succeeded,
    OperationItemFailure Failure,
    PayloadFacts? VerifiedDestination);

public sealed record OperationPlanResult(
    OperationPlanId PlanId,
    OperationPlanState State,
    IReadOnlyList<OperationItemResult> Items);

public enum OperationPrepareStatus
{
    Created = 0,
    Existing = 1,
    IdempotencyConflict = 2,
}

public sealed record OperationPrepareResult(
    OperationPrepareStatus Status,
    PreparedOperationPlan Plan);

public enum OperationAcquireStatus
{
    Acquired = 0,
    Existing = 1,
    NotFound = 2,
    ConfirmationMismatch = 3,
    NotReady = 4,
    NotCurrent = 5,
}

public sealed record OperationAcquireResult(
    OperationAcquireStatus Status,
    PreparedOperationPlan? Plan = null,
    OperationPlanResult? ExistingResult = null);

public enum OperationCompletionStatus
{
    Accepted = 0,
    NotCurrent = 1,
}

public sealed record OperationCompletionResult(
    OperationCompletionStatus Status,
    OperationPlanResult? Result);

public sealed record OperationAuditEvent(
    OperationPlanId PlanId,
    OperationPlanState State,
    int SucceededItems,
    int FailedItems);

internal static class OperationContractRules
{
    public static Guid NonEmpty(Guid value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty, parameterName);
        return value;
    }

    public static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The value is required.",
                parameterName);
        }

        var isInvalid = !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Contains('\0')
            || value.Length > maximumLength;
        if (isInvalid)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value must be trimmed, contain no NUL and be at most {maximumLength} characters.");
        }

        return value;
    }
}
