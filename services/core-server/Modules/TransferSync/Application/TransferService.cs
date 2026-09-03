using AssetLibrary.Modules.TransferSync.Contracts;
using AssetLibrary.Modules.TransferSync.Domain;

namespace AssetLibrary.Modules.TransferSync.Application;

public sealed class TransferService(
    ITransferSessionStore sessionStore,
    ITransferPayloadPort payloadPort,
    ITransferEventSink eventSink,
    TimeProvider timeProvider,
    TransferExecutionLimits limits)
{
    public async ValueTask<TransferResult> ExecuteAsync(
        TransferRequest request,
        TransferExecutionRight executionRight,
        CancellationToken cancellationToken)
    {
        Validate(request, executionRight);
        var now = timeProvider.GetUtcNow();
        if (executionRight.ExpiresAt <= now)
        {
            return Record(TransferStatePolicy.Stale(request.SessionId));
        }

        if (request.Deadline.ToUniversalTime() <= now)
        {
            return Record(TransferStatePolicy.Cancelled(request.SessionId, deadlineExceeded: true));
        }

        TransferStartResult start;
        using (var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            startTimeout.CancelAfter(
                Min(
                    limits.OperationTimeout,
                    request.Deadline.ToUniversalTime() - now,
                    executionRight.ExpiresAt - now));
            try
            {
                start = await sessionStore.TryStartAsync(
                    request,
                    executionRight,
                    now,
                    startTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Record(
                    TransferStatePolicy.Cancelled(request.SessionId, deadlineExceeded: false));
            }
            catch (OperationCanceledException) when (startTimeout.IsCancellationRequested)
            {
                return Record(TimeoutOrStale(request, executionRight));
            }
        }

        switch (start.Status)
        {
            case TransferStartStatus.Existing:
                return Existing(request, start);
            case TransferStartStatus.IdempotencyConflict:
                EnsureNoExistingPayload(start);
                return Record(TransferStatePolicy.IdempotencyConflict(request.SessionId));
            case TransferStartStatus.NotCurrent:
                EnsureNoExistingPayload(start);
                return Record(TransferStatePolicy.Stale(request.SessionId));
            case TransferStartStatus.Started
                when start.ExistingRequest is null && start.ExistingResult is null:
                break;
            case TransferStartStatus.Started:
                throw new InvalidOperationException(
                    "A newly started transfer cannot include an existing result.");
            default:
                throw new InvalidOperationException("The transfer store returned an unknown start status.");
        }

        var deadline = request.Deadline.ToUniversalTime();
        var executionStartedAt = timeProvider.GetUtcNow();
        TransferResult result;
        if (deadline <= executionStartedAt)
        {
            result = TransferStatePolicy.Cancelled(request.SessionId, deadlineExceeded: true);
        }
        else if (executionRight.ExpiresAt <= executionStartedAt)
        {
            result = TransferStatePolicy.Stale(request.SessionId);
        }
        else
        {
            var allowed = Min(
                limits.OperationTimeout,
                deadline - executionStartedAt,
                executionRight.ExpiresAt - executionStartedAt);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(allowed);
            try
            {
                var receipt = await payloadPort.TransferAsync(
                    request,
                    executionRight,
                    timeout.Token).ConfigureAwait(false);
                result = TransferStatePolicy.FromReceipt(request, receipt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result = TransferStatePolicy.Cancelled(request.SessionId, deadlineExceeded: false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var cancelledAt = timeProvider.GetUtcNow();
                if (!timeout.IsCancellationRequested
                    && deadline > cancelledAt
                    && executionRight.ExpiresAt > cancelledAt)
                {
                    throw;
                }

                result = TimeoutOrStale(request, executionRight);
            }
        }

        if (!TransferStatePolicy.CanTransition(
            TransferSessionState.Executing,
            result.State))
        {
            throw new InvalidOperationException("The transfer result regressed its state.");
        }

        using var completionTimeout = new CancellationTokenSource(limits.OperationTimeout);
        var completion = await sessionStore.CompleteAsync(
            request.SessionId,
            executionRight,
            result,
            timeProvider.GetUtcNow(),
            completionTimeout.Token).ConfigureAwait(false);
        if (completion.Status == TransferCompletionStatus.NotCurrent)
        {
            return Record(TransferStatePolicy.Stale(request.SessionId));
        }

        if (completion.Status != TransferCompletionStatus.Accepted
            || completion.Result != result)
        {
            throw new InvalidOperationException("The transfer store returned an invalid completion.");
        }

        return Record(result);
    }

    private static void Validate(
        TransferRequest request,
        TransferExecutionRight executionRight)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExpectedSource);
        if (request.SessionId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(request.IdempotencyKey.Value)
            || string.IsNullOrWhiteSpace(request.Source.Value)
            || string.IsNullOrWhiteSpace(request.Target.Value)
            || string.IsNullOrWhiteSpace(request.ExpectedSource.Sha256.Value)
            || string.IsNullOrWhiteSpace(executionRight.Worker.Value)
            || executionRight.Token.Value == Guid.Empty
            || executionRight.Generation <= 0)
        {
            throw new ArgumentException("The transfer contains a default value object.", nameof(request));
        }

        if (request.Source == request.Target)
        {
            throw new ArgumentException(
                "Source and target tokens must differ.",
                nameof(request));
        }

        if (request.Deadline == default)
        {
            throw new ArgumentException("A transfer deadline is required.", nameof(request));
        }

        if (executionRight.ExpiresAt == default)
        {
            throw new ArgumentException(
                "An execution right expiry is required.",
                nameof(executionRight));
        }
    }

    private static TimeSpan Min(params TimeSpan[] candidates)
    {
        var result = candidates[0];
        foreach (var candidate in candidates)
        {
            if (candidate < result)
            {
                result = candidate;
            }
        }

        return result <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : result;
    }

    private TransferResult Existing(TransferRequest request, TransferStartResult start)
    {
        var existingRequest = start.ExistingRequest
            ?? throw new InvalidOperationException(
                "An existing transfer must include its original request.");
        var existingResult = start.ExistingResult
            ?? throw new InvalidOperationException(
                "An existing transfer must include its stable result.");
        if (!TransferStatePolicy.HasSameIntent(request, existingRequest))
        {
            throw new InvalidOperationException(
                "The transfer store returned different idempotent semantics.");
        }

        TransferStatePolicy.ValidateStoredResult(existingRequest, existingResult);
        return Record(existingResult);
    }

    private TransferResult TimeoutOrStale(
        TransferRequest request,
        TransferExecutionRight executionRight) =>
        executionRight.ExpiresAt <= timeProvider.GetUtcNow()
            ? TransferStatePolicy.Stale(request.SessionId)
            : TransferStatePolicy.Cancelled(request.SessionId, deadlineExceeded: true);

    private static void EnsureNoExistingPayload(TransferStartResult start)
    {
        if (start.ExistingRequest is not null || start.ExistingResult is not null)
        {
            throw new InvalidOperationException(
                "This transfer start status cannot include an existing payload.");
        }
    }

    private TransferResult Record(TransferResult result)
    {
        eventSink.Record(
            new TransferAuditEvent(
                result.SessionId,
                result.State,
                result.Failure,
                result.BytesTransferred));
        return result;
    }
}
