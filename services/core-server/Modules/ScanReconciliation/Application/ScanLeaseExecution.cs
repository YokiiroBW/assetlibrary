using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

internal sealed class ScanLeaseExecution(
    DurableTaskLease lease, IDurableTaskCoordinator tasks, IDurableTaskCommitGuard guard,
    InitialScanExecutionOptions options, TimeProvider timeProvider, CancellationTokenSource workCancellation) : IAsyncDisposable
{
    private readonly SemaphoreSlim checkpoint = new(1, 1);
    private bool committing;
    public bool CancellationRequested { get; private set; }
    public bool LeaseLost { get; private set; }

    public async Task MonitorAsync()
    {
        try
        {
            while (!workCancellation.IsCancellationRequested)
            {
                await Task.Delay(options.HeartbeatInterval, timeProvider, workCancellation.Token).ConfigureAwait(false);
                await ConfirmAsync(workCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (workCancellation.IsCancellationRequested)
        {
            // Shutdown and durable cancellation terminate the monitor with its owned scan.
        }
        catch (Exception)
        {
            LeaseLost = true;
            await workCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask ConfirmAsync(CancellationToken token)
    {
        await checkpoint.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (committing)
            {
                return;
            }

            var result = await tasks.HeartbeatAsync(HeartbeatRequest(), token).ConfigureAwait(false);
            CancellationRequested = result.CancellationRequested;
            LeaseLost = result.Status != TaskLeaseMutationStatus.Accepted;
            if (CancellationRequested || LeaseLost)
            {
                await workCancellation.CancelAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            checkpoint.Release();
        }
    }

    public async ValueTask<int> CommitAsync(Func<CancellationToken, ValueTask<int>> commit, CancellationToken token)
    {
        await checkpoint.WaitAsync(token).ConfigureAwait(false);
        try
        {
            committing = true;
            return await guard.CommitAsync(HeartbeatRequest(), commit, token).ConfigureAwait(false);
        }
        catch (TaskCommitLeaseException exception)
        {
            CancellationRequested = exception.Code == "scan_cancelled";
            LeaseLost = !CancellationRequested;
            throw new FileDiscoveryException(exception.Code);
        }
        finally
        {
            checkpoint.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        checkpoint.Dispose();
        return ValueTask.CompletedTask;
    }

    private DurableTaskHeartbeatRequest HeartbeatRequest() => new(lease.TaskId, lease.Identity, options.LeaseDuration);
}
