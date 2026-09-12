using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

internal sealed class ThumbnailSession(ClientTransport transport, SnapshotStore snapshots,
    Func<byte[], CancellationToken, Task<ThumbnailPixels>> decode) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource stopped = new();
    private readonly List<Task> work = [];
    private readonly SemaphoreSlim processing = new(1, 1);
    private bool disposed;

    internal Task<ThumbnailResponse> ReadAsync(ThumbnailRequest request, CancellationToken token)
    {
        lock (gate)
        {
            if (disposed) { return Task.FromResult(Failure(request, ThumbnailStatus.Unavailable)); }
            work.RemoveAll(task => task.IsCompleted);
            if (work.Count == 4) { return Task.FromResult(Failure(request, ThumbnailStatus.Busy)); }
            // Even cached HTTP content or a synchronous decoder must never execute under the caller's session lock.
            var task = Task.Run(() => LoadAsync(request, token), CancellationToken.None);
            work.Add(task);
            _ = task.ContinueWith(completed => { lock (gate) { work.Remove(completed); } }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private async Task<ThumbnailResponse> LoadAsync(ThumbnailRequest request, CancellationToken token)
    {
        var status = snapshots.ResolveThumbnail(request, out var target, out var epochToken);
        if (status != ThumbnailStatus.Ready) { return Failure(request, status); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, epochToken, stopped.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var entered = false;
        try
        {
            await processing.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            var bytes = await transport.ThumbnailAsync(target!.Library, target.Entry, deadline.Token).ConfigureAwait(false);
            _ = PngThumbnailContainer.Validate(bytes);
            var pixels = await decode(bytes, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            status = snapshots.ResolveThumbnail(request, out _, out _);
            return status == ThumbnailStatus.Ready
                ? new ThumbnailResponse(status, request.Epoch, request.Node, pixels) { Validity = epochToken } : Failure(request, status);
        }
        catch (ThumbnailHttpException failure)
        {
            if (failure.InvalidatesSession) { snapshots.RevokeSession(); return Failure(request, ThumbnailStatus.AccessDenied); }
            return Failure(request, failure.Status switch
            {
                404 or 415 or 422 => ThumbnailStatus.Unsupported,
                429 => ThumbnailStatus.Busy,
                _ => ThumbnailStatus.Unavailable,
            });
        }
        catch (InvalidDataException) { return Failure(request, ThumbnailStatus.InvalidResponse); }
        catch (Exception failure) when (failure is IOException or OperationCanceledException or TimeoutException)
        { return Failure(request, request.Epoch != snapshots.CurrentEpoch ? ThumbnailStatus.Expired : ThumbnailStatus.Unavailable); }
        finally { if (entered) { processing.Release(); } }
    }

    private ThumbnailResponse Failure(ThumbnailRequest request, ThumbnailStatus status) => new(status, snapshots.CurrentEpoch, request.Node);

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (gate) { disposed = true; pending = [.. work]; }
        await stopped.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        finally { processing.Dispose(); stopped.Dispose(); }
    }
}
