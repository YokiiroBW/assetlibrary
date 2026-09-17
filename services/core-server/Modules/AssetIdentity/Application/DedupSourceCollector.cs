using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Domain;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Collects observations for the accepted sources. Discovery failures are recorded as an incomplete
/// scan, so a directory that could not be read is never presented as a directory without files.
/// </summary>
internal sealed class DedupSourceCollector(IDedupFileDiscovery discovery)
{
    public async Task<List<DedupAnalysisEntry>> CollectAsync(
        IReadOnlyList<DedupSourceRequest> sources,
        DedupAnalysisLimits limits,
        List<DedupSourceFailure> failures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(failures);
        var entries = new List<DedupAnalysisEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            try
            {
                await foreach (var file in discovery
                    .DiscoverAsync(source.Root, cancellationToken)
                    .ConfigureAwait(false))
                {
                    var identity = ContentSignature.BuildSourceIdentity(source.Root, file.RelativePath);
                    if (!seen.Add(identity))
                    {
                        continue;
                    }

                    if (entries.Count >= limits.MaximumFiles)
                    {
                        failures.Add(new DedupSourceFailure(source.SourceId, "file_ceiling_reached"));
                        break;
                    }

                    entries.Add(new DedupAnalysisEntry(
                        identity,
                        source.SourceId,
                        source.Root,
                        file.RelativePath,
                        file.IsReparsePoint,
                        file.IsExcluded,
                        file.Length,
                        file.LastWriteTimeUtc,
                        Signature: null,
                        DedupItemReadState.NotRead,
                        DedupReadFailure.None,
                        DedupSkipReason.None));
                }
            }
            catch (DedupDiscoveryException exception)
            {
                failures.Add(new DedupSourceFailure(source.SourceId, exception.FailureCode));
            }
        }

        return entries;
    }
}

/// <summary>
/// Decides which files are worth a content read and folds the read results back into the entries.
/// The length bucket is only a pre-filter: a single-length file can never be a byte duplicate, and
/// no read is spent proving it.
/// </summary>
internal static class DedupReadPlanner
{
    public static List<DedupAnalysisEntry> Plan(
        List<DedupAnalysisEntry> entries,
        IReadOnlyDictionary<long, int> buckets,
        DedupAnalysisLimits limits,
        out bool scanBoundsReached)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(buckets);
        var planned = new List<DedupAnalysisEntry>(entries.Count);
        var attemptedFiles = 0;
        var boundsReached = false;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var skip = DedupAnalysisPolicy.SkipReasonFor(entry, buckets);
            if (skip != DedupSkipReason.None)
            {
                entries[index] = entry with { SkipReason = skip };
                continue;
            }

            // Only the per-file ceiling and the file count are decided here. A file's own size is the one
            // number discovery can still speak for, and the count is what bounds the run's work.
            if (entry.Length > limits.MaximumFileBytes || attemptedFiles >= limits.MaximumFiles)
            {
                entries[index] = entry with { SkipReason = DedupSkipReason.ExceedsBudget };
                boundsReached = true;
                continue;
            }

            // The byte ceiling is deliberately not decided here. The length discovery saw is a guess about
            // a file that may have changed since, so admitting files by it would both reject files the
            // budget could have afforded and admit files it cannot. It is settled on the bytes the reads
            // really consume, which is the only number the run can be held to, and running out of it is
            // reported instead of being silently rounded down to "unique".
            attemptedFiles++;
            planned.Add(entry with { ReadState = DedupItemReadState.SkippedByBudget });
        }

        scanBoundsReached = boundsReached;
        return planned;
    }

    public static List<DedupAnalysisEntry> Apply(
        IReadOnlyList<DedupAnalysisEntry> baseEntries,
        IReadOnlyList<DedupAnalysisEntry> planned,
        DedupContentReadOutcome outcome,
        IReadOnlyDictionary<long, int> buckets)
    {
        ArgumentNullException.ThrowIfNull(baseEntries);
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(buckets);
        var resolved = new Dictionary<string, DedupAnalysisEntry>(StringComparer.Ordinal);
        var reads = outcome.Reads;
        for (var index = 0; index < outcome.Attempted.Count; index++)
        {
            var entry = outcome.Attempted[index];
            var read = reads[index];
            if (!string.Equals(read.RelativePath.Value, entry.RelativePath.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The content reader must answer its requests in the order they were submitted.");
            }

            resolved[entry.SourceIdentity] = read.Failure == DedupReadFailure.None && read.Sha256 is not null
                ? entry with
                {
                    // The length a read verified replaces the length discovery saw. Keeping the stale number
                    // would make the preview and its byte total describe a file that is no longer there.
                    Length = read.Length,
                    Signature = new ContentSignature(
                        read.Length,
                        new Sha256Digest(read.Sha256),
                        read.StructureHash,
                        read.LastWriteTimeUtc,
                        entry.SourceIdentity),
                    ReadState = DedupItemReadState.ContentVerified,
                }
                : entry with
                {
                    // A refused read is charged nothing. The entry keeps the length the answer state it found
                    // because that is what the preview has to explain, but no byte of it was read, so the
                    // read volume must never claim it: only a verified read contributes there.
                    Length = read.Length,
                    ReadState = DedupItemReadState.ReadFailed,
                    Failure = read.Failure,
                };
        }

        var skipped = SkipReasons(planned);

        var result = new List<DedupAnalysisEntry>(baseEntries.Count);
        foreach (var entry in baseEntries)
        {
            if (resolved.TryGetValue(entry.SourceIdentity, out var read))
            {
                result.Add(read);
                continue;
            }

            // The base entry is what the plan and the read pass worked from, so a file the settled budget
            // never requested keeps the skip reason they gave it instead of looking like an untouched file.
            var reason = skipped.TryGetValue(entry.SourceIdentity, out var marked)
                ? marked
                : entry.SkipReason;
            if (reason == DedupSkipReason.None)
            {
                reason = DedupAnalysisPolicy.SkipReasonFor(entry, buckets);
            }

            result.Add(entry with { SkipReason = reason, ReadState = ReasonState(entry, reason) });
        }

        return result;
    }

    private static Dictionary<string, DedupSkipReason> SkipReasons(IReadOnlyList<DedupAnalysisEntry> planned)
    {
        var skipped = new Dictionary<string, DedupSkipReason>(StringComparer.Ordinal);
        foreach (var entry in planned)
        {
            if (entry.SkipReason != DedupSkipReason.None)
            {
                skipped[entry.SourceIdentity] = entry.SkipReason;
            }
        }

        return skipped;
    }

    private static DedupItemReadState ReasonState(DedupAnalysisEntry entry, DedupSkipReason reason) =>
        entry.ReadState == DedupItemReadState.NotRead && reason != DedupSkipReason.None
            ? DedupItemReadState.SkippedByBudget
            : entry.ReadState;

    public static DedupAnalysisStatus SelectStatus(
        IReadOnlyList<DedupAnalysisEntry> entries,
        IReadOnlyList<DedupSourceFailure> failures)
    {
        if (entries.Count == 0)
        {
            return failures.Count > 0 ? DedupAnalysisStatus.PartiallyAnalyzed : DedupAnalysisStatus.Empty;
        }

        if (entries.All(entry => entry.ReadState == DedupItemReadState.ReadFailed))
        {
            return DedupAnalysisStatus.Failed;
        }

        // Only "never considered" reasons keep a run complete: a reparse point and a file with a
        // unique length are fully accounted for, while a file that moved away, an exhausted budget
        // or an aborted traversal leaves the preview partial and therefore not final.
        var complete = failures.Count == 0
            && entries.All(entry => entry.ReadState == DedupItemReadState.ContentVerified
                || entry.SkipReason is DedupSkipReason.NoLengthCandidate
                    or DedupSkipReason.ReparsePoint
                    or DedupSkipReason.ExcludedByDiscoveryPolicy);
        return complete ? DedupAnalysisStatus.Completed : DedupAnalysisStatus.PartiallyAnalyzed;
    }
}
