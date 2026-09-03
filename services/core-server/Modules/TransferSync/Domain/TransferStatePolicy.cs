using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.TransferSync.Domain;

public static class TransferStatePolicy
{
    public static bool HasSameIntent(TransferRequest left, TransferRequest right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.IdempotencyKey == right.IdempotencyKey
            && left.Source == right.Source
            && left.Target == right.Target
            && left.ExpectedSource == right.ExpectedSource
            && left.Deadline.ToUniversalTime() == right.Deadline.ToUniversalTime();
    }

    public static bool IsTerminal(TransferSessionState state) =>
        state is TransferSessionState.Completed
            or TransferSessionState.Failed
            or TransferSessionState.Cancelled
            or TransferSessionState.Conflict
            or TransferSessionState.ManualReview;

    public static bool CanTransition(
        TransferSessionState current,
        TransferSessionState next) =>
        current switch
        {
            TransferSessionState.Pending => next is TransferSessionState.Executing
                or TransferSessionState.Cancelled
                or TransferSessionState.Conflict
                or TransferSessionState.ManualReview,
            TransferSessionState.Executing => next is TransferSessionState.Verifying
                or TransferSessionState.Completed
                or TransferSessionState.Failed
                or TransferSessionState.Cancelled
                or TransferSessionState.Conflict
                or TransferSessionState.ManualReview,
            TransferSessionState.Verifying => next is TransferSessionState.Completed
                or TransferSessionState.Failed
                or TransferSessionState.Cancelled
                or TransferSessionState.Conflict
                or TransferSessionState.ManualReview,
            _ => false,
        };

    public static TransferResult FromReceipt(
        TransferRequest request,
        TransferPortReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.SessionId != request.SessionId)
        {
            throw new InvalidOperationException("The transfer adapter returned another session.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(receipt.BytesTransferred);
        if (receipt.BytesTransferred > request.ExpectedSource.Length)
        {
            throw new InvalidOperationException("The transfer adapter exceeded the expected byte count.");
        }

        return receipt.Outcome switch
        {
            TransferPortOutcome.Completed => Completed(request, receipt),
            TransferPortOutcome.TargetExists => Failure(
                request,
                receipt,
                TransferSessionState.Conflict,
                TransferFailureKind.TargetExists),
            TransferPortOutcome.SourceChanged => Failure(
                request,
                receipt,
                TransferSessionState.Conflict,
                TransferFailureKind.SourceChanged),
            TransferPortOutcome.HashMismatch => Failure(
                request,
                receipt,
                TransferSessionState.Failed,
                TransferFailureKind.HashMismatch),
            TransferPortOutcome.InsufficientSpace => Failure(
                request,
                receipt,
                TransferSessionState.Failed,
                TransferFailureKind.InsufficientSpace),
            TransferPortOutcome.UnsafeSandbox => Failure(
                request,
                receipt,
                TransferSessionState.Failed,
                TransferFailureKind.UnsafeSandbox),
            TransferPortOutcome.ManualReviewRequired => Failure(
                request,
                receipt,
                TransferSessionState.ManualReview,
                TransferFailureKind.ManualReviewRequired),
            _ => throw new InvalidOperationException("The transfer adapter returned an unknown outcome."),
        };
    }

    public static TransferResult Cancelled(
        TransferSessionId sessionId,
        bool deadlineExceeded) =>
        new(
            sessionId,
            TransferSessionState.Cancelled,
            deadlineExceeded
                ? TransferFailureKind.DeadlineExceeded
                : TransferFailureKind.Cancelled,
            0,
            null);

    public static TransferResult Stale(TransferSessionId sessionId) =>
        new(
            sessionId,
            TransferSessionState.ManualReview,
            TransferFailureKind.StaleExecutionRight,
            0,
            null);

    public static TransferResult IdempotencyConflict(TransferSessionId sessionId) =>
        new(
            sessionId,
            TransferSessionState.Conflict,
            TransferFailureKind.IdempotencyConflict,
            0,
            null);

    public static void ValidateStoredResult(
        TransferRequest request,
        TransferResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.State)
            || !Enum.IsDefined(result.Failure)
            || result.SessionId != request.SessionId
            || result.BytesTransferred < 0
            || result.BytesTransferred > request.ExpectedSource.Length)
        {
            throw new InvalidOperationException("The transfer store returned an invalid result.");
        }

        if (result.State == TransferSessionState.Completed)
        {
            if (result.Failure != TransferFailureKind.None
                || result.BytesTransferred != request.ExpectedSource.Length
                || result.VerifiedTarget != request.ExpectedSource)
            {
                throw new InvalidOperationException(
                    "The transfer store returned an invalid completed result.");
            }

            return;
        }

        var executingIsValid = result.State == TransferSessionState.Executing
            && result.Failure == TransferFailureKind.None;
        var terminalFailureIsValid = HasCoherentFailure(result.State, result.Failure);
        if ((!executingIsValid && !terminalFailureIsValid)
            || result.VerifiedTarget is not null)
        {
            throw new InvalidOperationException(
                "A non-completed stored transfer cannot claim a verified target.");
        }
    }

    private static TransferResult Completed(
        TransferRequest request,
        TransferPortReceipt receipt)
    {
        if (receipt.BytesTransferred != request.ExpectedSource.Length
            || receipt.VerifiedTarget != request.ExpectedSource)
        {
            throw new InvalidOperationException(
                "A completed transfer must return the exact reopened target facts.");
        }

        return new(
            request.SessionId,
            TransferSessionState.Completed,
            TransferFailureKind.None,
            receipt.BytesTransferred,
            receipt.VerifiedTarget);
    }

    private static TransferResult Failure(
        TransferRequest request,
        TransferPortReceipt receipt,
        TransferSessionState state,
        TransferFailureKind failure)
    {
        if (receipt.VerifiedTarget is not null)
        {
            throw new InvalidOperationException(
                "A failed transfer cannot claim a verified target.");
        }

        return new(request.SessionId, state, failure, receipt.BytesTransferred, null);
    }

    private static bool HasCoherentFailure(
        TransferSessionState state,
        TransferFailureKind failure) =>
        state switch
        {
            TransferSessionState.Failed => failure is TransferFailureKind.HashMismatch
                or TransferFailureKind.InsufficientSpace
                or TransferFailureKind.UnsafeSandbox,
            TransferSessionState.Cancelled => failure is TransferFailureKind.Cancelled
                or TransferFailureKind.DeadlineExceeded,
            TransferSessionState.Conflict => failure is TransferFailureKind.IdempotencyConflict
                or TransferFailureKind.TargetExists
                or TransferFailureKind.SourceChanged,
            TransferSessionState.ManualReview => failure is TransferFailureKind.StaleExecutionRight
                or TransferFailureKind.ManualReviewRequired,
            _ => false,
        };
}
