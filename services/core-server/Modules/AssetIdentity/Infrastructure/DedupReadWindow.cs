using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Infrastructure;

/// <summary>
/// Runs content reads through a bounded window. The window is what keeps a read-only preview from
/// saturating a NAS link or the host CPU; it needs no extra runtime dependency, and it never
/// changes the order of the answers it returns.
/// </summary>
internal static class DedupReadWindow
{
    public static async Task<IReadOnlyList<TResult>> RunAsync<TRequest, TResult>(
        IReadOnlyList<TRequest> requests,
        int concurrency,
        Func<TRequest, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(read);
        var results = new TResult[requests.Count];
        var window = Math.Max(Math.Min(concurrency, Math.Max(requests.Count, 1)), 1);
        var pending = new Dictionary<int, Task<TResult>>(window);
        var next = 0;
        while (next < requests.Count || pending.Count > 0)
        {
            while (pending.Count < window && next < requests.Count)
            {
                var index = next++;
                pending[index] = Task.Run(
                    () => read(requests[index], cancellationToken),
                    cancellationToken);
            }

            var finished = await Task.WhenAny(pending.Values).ConfigureAwait(false);
            var key = pending.First(pair => ReferenceEquals(pair.Value, finished)).Key;
            pending.Remove(key);
            results[key] = await finished.ConfigureAwait(false);
        }

        return results;
    }
}
