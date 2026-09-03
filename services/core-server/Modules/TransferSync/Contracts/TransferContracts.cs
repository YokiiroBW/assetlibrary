namespace AssetLibrary.Modules.TransferSync.Contracts;

public readonly record struct TransferSessionId
{
    public TransferSessionId(Guid value)
    {
        Value = TransferContractRules.Identifier(value, nameof(value));
    }

    public Guid Value { get; }

    public static TransferSessionId New() => new(Guid.NewGuid());
}

public readonly record struct TransferLeaseToken
{
    public TransferLeaseToken(Guid value)
    {
        Value = TransferContractRules.Identifier(value, nameof(value));
    }

    public Guid Value { get; }

    public static TransferLeaseToken New() => new(Guid.NewGuid());
}

public readonly record struct TransferIdempotencyKey
{
    public TransferIdempotencyKey(string value)
    {
        Value = TransferContractRules.Required(value, 200, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct TransferLocationToken
{
    public TransferLocationToken(string value)
    {
        Value = TransferContractRules.OpaqueToken(value, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct Sha256Digest
{
    public const int HexLength = 64;

    public Sha256Digest(string value)
    {
        Value = TransferContractRules.LowercaseSha256(value, nameof(value));
    }

    public string Value { get; }
}

public sealed record PayloadFacts
{
    public PayloadFacts(long length, Sha256Digest sha256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Length = length;
        Sha256 = new Sha256Digest(sha256.Value);
    }

    public long Length { get; }

    public Sha256Digest Sha256 { get; }
}

public readonly record struct TransferWorker
{
    public TransferWorker(string value)
    {
        Value = TransferContractRules.LowercaseIdentifier(value, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct TransferExecutionRight
{
    public TransferExecutionRight(
        TransferWorker worker,
        TransferLeaseToken token,
        long generation,
        DateTimeOffset expiresAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        if (expiresAt == default)
        {
            throw new ArgumentException("An execution right expiry is required.", nameof(expiresAt));
        }

        Worker = new TransferWorker(worker.Value);
        Token = new TransferLeaseToken(token.Value);
        Generation = generation;
        ExpiresAt = expiresAt.ToUniversalTime();
    }

    public TransferWorker Worker { get; }

    public TransferLeaseToken Token { get; }

    public long Generation { get; }

    public DateTimeOffset ExpiresAt { get; }
}

public sealed record TransferRequest(
    TransferSessionId SessionId,
    TransferIdempotencyKey IdempotencyKey,
    TransferLocationToken Source,
    TransferLocationToken Target,
    PayloadFacts ExpectedSource,
    DateTimeOffset Deadline);

public enum TransferSessionState
{
    Pending = 0,
    Executing = 1,
    Verifying = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Conflict = 6,
    ManualReview = 7,
}

public enum TransferFailureKind
{
    None = 0,
    IdempotencyConflict = 1,
    TargetExists = 2,
    SourceChanged = 3,
    HashMismatch = 4,
    InsufficientSpace = 5,
    Cancelled = 6,
    DeadlineExceeded = 7,
    StaleExecutionRight = 8,
    UnsafeSandbox = 9,
    ManualReviewRequired = 10,
}

public sealed record TransferResult(
    TransferSessionId SessionId,
    TransferSessionState State,
    TransferFailureKind Failure,
    long BytesTransferred,
    PayloadFacts? VerifiedTarget);

public enum TransferPortOutcome
{
    Completed = 0,
    TargetExists = 1,
    SourceChanged = 2,
    HashMismatch = 3,
    InsufficientSpace = 4,
    UnsafeSandbox = 5,
    ManualReviewRequired = 6,
}

public sealed record TransferPortReceipt(
    TransferSessionId SessionId,
    TransferPortOutcome Outcome,
    long BytesTransferred,
    PayloadFacts? VerifiedTarget);

public enum TransferStartStatus
{
    Started = 0,
    Existing = 1,
    IdempotencyConflict = 2,
    NotCurrent = 3,
}

public sealed record TransferStartResult(
    TransferStartStatus Status,
    TransferRequest? ExistingRequest = null,
    TransferResult? ExistingResult = null);

public enum TransferCompletionStatus
{
    Accepted = 0,
    NotCurrent = 1,
}

public sealed record TransferCompletionResult(
    TransferCompletionStatus Status,
    TransferResult? Result);

public sealed record TransferAuditEvent(
    TransferSessionId SessionId,
    TransferSessionState State,
    TransferFailureKind Failure,
    long BytesTransferred);

internal static class TransferContractRules
{
    public static Guid Identifier(Guid value, string parameterName) =>
        value != Guid.Empty
            ? value
            : throw new ArgumentException("The identifier cannot be empty.", parameterName);

    public static string Required(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value cannot exceed {maximumLength} characters.");
        }

        if (value.AsSpan().Trim().Length != value.Length || value.Contains('\0'))
        {
            throw new ArgumentException(
                "The value must be trimmed and contain no NUL.",
                parameterName);
        }

        return value;
    }

    public static string OpaqueToken(string value, string parameterName)
    {
        var candidate = Required(value, 160, parameterName);
        if (candidate.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "A transfer location token must be opaque URL-safe text.",
                parameterName);
        }

        return candidate;
    }

    public static string LowercaseSha256(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != Sha256Digest.HexLength
            || value.Any(character =>
                !char.IsAsciiDigit(character) && character is < 'a' or > 'f'))
        {
            throw new ArgumentException(
                "A SHA-256 digest must contain exactly 64 lowercase hexadecimal characters.",
                parameterName);
        }

        return value;
    }

    public static string LowercaseIdentifier(string value, string parameterName)
    {
        var candidate = Required(value, 120, parameterName);
        if (!char.IsAsciiLetterLower(candidate[0])
            || candidate.Any(character =>
                !char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character)
                && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "A transfer worker must be a lowercase ASCII identifier.",
                parameterName);
        }

        return candidate;
    }
}
