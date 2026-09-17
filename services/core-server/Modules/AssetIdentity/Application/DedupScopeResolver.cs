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
/// explicit, and settles the byte ceiling on the bytes a read really consumed. The length recorded
/// during discovery is only what the file was then: it decides what the plan admits, but the charge is
/// always the bytes a read returned. Each read is therefore permitted only what the aggregate budget
/// still has unspent, so a file that grew since it was observed cannot be read in full on a budget that
/// never covered it, and a reader that answers outside its allowance leaves an explicitly bounded run
/// instead of a total that quietly disagrees with the bytes on disk.
/// </summary>
internal sealed class DedupContentReadBatch(IDedupContentReader contentReader)
{
    public async ValueTask<DedupContentReadOutcome> ReadAsync(
        List<DedupAnalysisEntry> planned,
        DedupAnalysisLimits limits,
        CancellationToken cancellationToken)
    {
        var results = new List<ContentReadResult>(planned.Count);
        var attempts = new List<DedupAnalysisEntry>(planned.Count);
        var batchSize = Math.Max(limits.HashConcurrency * 8, 16);
        var remaining = limits.MaximumBytes;
        var overrun = false;

        // A plan whose files together claim more than the allowance can only be read in part, and that is a
        // bounded run whichever batch the allowance runs out in. Deciding it from the plan itself keeps the
        // disclosure independent of how the reads happened to be grouped.
        var refused = planned.Sum(entry => entry.Length) > limits.MaximumBytes;
        var index = 0;
        while (index < planned.Count && remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var last = Math.Min(index + batchSize, planned.Count);
            var requests = new List<DedupContentReadRequest>(last - index);
            var granted = new List<int>(last - index);
            for (var position = index; position < last; position++)
            {
                var entry = planned[position];

                // The permit is the smaller of the per-file ceiling, the length the plan admitted, and what
                // the aggregate allowance still has unspent. The last of the three is what keeps a run inside
                // the bytes it was given: no batch may promise more than the budget still holds, so a file
                // that can no longer be afforded in whole is refused rather than read on bytes nobody
                // granted it.
                var unspent = remaining > int.MaxValue ? int.MaxValue : (int)remaining;
                var permit = (int)Math.Min(limits.MaximumFileBytes, Math.Min(entry.Length, unspent));
                requests.Add(new DedupContentReadRequest(
                    entry.SourceIdentity,
                    entry.Root,
                    entry.RelativePath,
                    permit));
                granted.Add(permit);
                remaining -= permit;
            }

            var read = await contentReader
                .ReadAsync(requests, limits.HashConcurrency, cancellationToken)
                .ConfigureAwait(false);
            if (read.Count != requests.Count)
            {
                throw new InvalidOperationException("The content reader must answer every request exactly once.");
            }

            // Each answer settles the budget: the bytes the read really returned are charged and the rest of
            // the permit was never spent, because a permit is permission rather than a reservation. A
            // refused read costs nothing at all, since nothing was read.
            for (var position = 0; position < read.Count; position++)
            {
                var result = read[position];
                remaining += granted[position] - Math.Min(result.Length, int.MaxValue);
                if (result.Failure == DedupReadFailure.TooLarge)
                {
                    refused = true;
                }

                if (result.Failure == DedupReadFailure.None && result.Length > granted[position])
                {
                    // The ceiling is not advisory. A reader that answered with more than its request
                    // allowed has spent bytes nobody authorised, so that answer is unusable and becomes a
                    // failed read: no later file is read and the run ends explicitly bounded.
                    results.Add(new ContentReadResult(
                        result.RelativePath,
                        granted[position],
                        Sha256: null,
                        result.StructureHash,
                        result.LastWriteTimeUtc,
                        DedupReadFailure.ChangedDuringRead));
                    attempts.Add(planned[index + position]);
                    overrun = true;
                    continue;
                }

                // The charge is the bytes the read really returned: the only bytes this run can be held to.
                results.Add(result);
                attempts.Add(planned[index + position]);
            }

            remaining = Math.Max(remaining, 0);
            index = last;
            if (overrun)
            {
                break;
            }
        }

        if (index < planned.Count)
        {
            // Files are still unread when the pass stops, whether because the settled budget ran out or
            // because the run refused to read past a permit. Either way the preview is bounded, and saying
            // so is what keeps an incomplete run from reading as a complete one.
            refused = true;
        }
        var plannedIdentities = new HashSet<string>(
            attempts.Select(entry => entry.SourceIdentity),
            StringComparer.Ordinal);
        for (var position = 0; position < planned.Count; position++)
        {
            // Whatever the settled budget never requested stays visible as an unread, budget-skipped entry
            // instead of disappearing from the preview or being charged as if it had been read.
            var entry = planned[position];
            if (!plannedIdentities.Contains(entry.SourceIdentity))
            {
                planned[position] = entry with
                {
                    ReadState = DedupItemReadState.SkippedByBudget,
                    SkipReason = DedupSkipReason.ExceedsBudget,
                };
            }
        }

        // The answers and the entries they belong to are reported together, in request order, because the
        // caller folds one onto the other positionally: a settled budget can leave planned files unread.
        return new DedupContentReadOutcome(results, attempts, overrun, refused);
    }
}

/// <summary>
/// What one read pass produced: the answers, in request order, the entries those answers belong to, and
/// the two ways a pass can end bounded — a reader that answered outside its allowance, and a file whose
/// real size no longer fitted the permit the unspent budget could grant. The entries are kept beside the
/// answers because a settled byte budget can leave planned entries unread, and the flags are what turn
/// that into an explicit bounded run instead of a silent one.
/// </summary>
internal sealed record DedupContentReadOutcome(
    IReadOnlyList<ContentReadResult> Reads,
    IReadOnlyList<DedupAnalysisEntry> Attempted,
    bool ByteCeilingOverrun,
    bool CeilingRefused);
