using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Domain;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Decides which submitted sources may be read at all. A managed library root, the publication
/// output area and a duplicate registration are refused before a single byte is read, so a preview
/// can never re-import the product's own output.
/// </summary>
internal sealed class DedupScopeResolver(
    IDedupSourceScopeQuery scopes,
    IDedupSourceAvailability availability)
{
    public async ValueTask<DedupAnalysisScope> ResolveAsync(
        IReadOnlyList<DedupSourceRequest> sources,
        CancellationToken cancellationToken)
    {
        var registered = await scopes.RegisteredRootsAsync(cancellationToken).ConfigureAwait(false);
        var outputs = await scopes.ManagedOutputRootsAsync(cancellationToken).ConfigureAwait(false);

        // The scope rules run first so a refusal names its real reason: an output-backflow root that
        // also happens to be offline must not be reported as merely unavailable.
        var scope = DedupScopePolicy.Resolve(sources, registered, outputs);
        var readable = new List<DedupSourceRequest>(scope.Accepted.Count);
        var rejected = new List<DedupRejectedSourceRequest>(scope.Rejected);
        foreach (var source in scope.Accepted)
        {
            var state = await availability.CheckAsync(source.Root, cancellationToken).ConfigureAwait(false);
            if (state == StorageAvailability.Online)
            {
                readable.Add(source);
                continue;
            }

            // An offline source is unreadable right now. Its files are not deleted, and reporting
            // them as missing content would be a claim this analysis cannot support.
            rejected.Add(new DedupRejectedSourceRequest(source, DedupSourceRejection.SourceUnavailable));
        }

        return new DedupAnalysisScope(readable, rejected);
    }
}

/// <summary>
/// Submits planned reads in bounded batches so the in-flight window and the cancellation scope stay
/// explicit. Every answer is checked against its request before it is accepted.
/// </summary>
internal sealed class DedupContentReadBatch(IDedupContentReader contentReader)
{
    public async ValueTask<IReadOnlyList<ContentReadResult>> ReadAsync(
        List<DedupAnalysisEntry> planned,
        DedupAnalysisLimits limits,
        CancellationToken cancellationToken)
    {
        var results = new List<ContentReadResult>(planned.Count);
        var batchSize = Math.Max(limits.HashConcurrency * 8, 16);
        for (var offset = 0; offset < planned.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requests = new DedupContentReadRequest[Math.Min(batchSize, planned.Count - offset)];
            for (var index = 0; index < requests.Length; index++)
            {
                var entry = planned[offset + index];
                requests[index] = new DedupContentReadRequest(
                    entry.SourceIdentity,
                    entry.Root,
                    entry.RelativePath,
                    limits.MaximumFileBytes);
            }

            var read = await contentReader
                .ReadAsync(requests, limits.HashConcurrency, cancellationToken)
                .ConfigureAwait(false);
            if (read.Count != requests.Length)
            {
                throw new InvalidOperationException("The content reader must answer every request exactly once.");
            }

            results.AddRange(read);
        }

        return results;
    }
}
