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
/// explicit. Every answer is checked against its request before it is accepted, and the byte ceiling is
/// re-accounted against the bytes actually read: the length recorded during discovery is only what the
/// file was then, and a file that grew since must not be able to spend a budget that was already used.
/// </summary>
internal sealed class DedupContentReadBatch(IDedupContentReader contentReader)
{
    public async ValueTask<DedupContentReadOutcome> ReadAsync(
        List<DedupAnalysisEntry> planned,
        DedupAnalysisLimits limits,
        CancellationToken cancellationToken)
    {
        var results = new List<ContentReadResult>(planned.Count);
        var batchSize = Math.Max(limits.HashConcurrency * 8, 16);
        var remaining = limits.MaximumBytes;
        for (var offset = 0; offset < planned.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requests = new List<DedupContentReadRequest>(Math.Min(batchSize, planned.Count - offset));
            for (var index = offset; index < Math.Min(offset + batchSize, planned.Count); index++)
            {
                var entry = planned[index];
                if (entry.Length > remaining)
                {
                    // The planned byte total already admitted this file, but the budget is spent on
                    // bytes that were really read instead of on lengths that were merely seen.
                    planned[index] = entry with { SkipReason = DedupSkipReason.ExceedsBudget };
                    continue;
                }

                remaining -= entry.Length;
                requests.Add(new DedupContentReadRequest(
                    entry.SourceIdentity,
                    entry.Root,
                    entry.RelativePath,
                    limits.MaximumFileBytes));
            }

            if (requests.Count == 0)
            {
                continue;
            }

            var read = await contentReader
                .ReadAsync(requests, limits.HashConcurrency, cancellationToken)
                .ConfigureAwait(false);
            if (read.Count != requests.Count)
            {
                throw new InvalidOperationException("The content reader must answer every request exactly once.");
            }

            results.AddRange(read);
        }

        var attempted = planned.Where(entry => entry.SkipReason != DedupSkipReason.ExceedsBudget).ToArray();
        return new DedupContentReadOutcome(results, attempted);
    }
}

/// <summary>
/// What one read pass produced: the answers, in request order, and the entries those answers belong to.
/// The two are kept together because a re-accounted budget can leave planned entries unread.
/// </summary>
internal sealed record DedupContentReadOutcome(
    IReadOnlyList<ContentReadResult> Reads,
    IReadOnlyList<DedupAnalysisEntry> Attempted);
