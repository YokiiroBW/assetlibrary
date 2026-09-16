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
        long attemptedBytes = 0;
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

            if (entry.Length > limits.MaximumFileBytes
                || attemptedFiles >= limits.MaximumFiles
                || attemptedBytes + entry.Length > limits.MaximumBytes)
            {
                // The budget is a ceiling on bytes and files actually read. Running out of it is
                // reported instead of being silently rounded down to "unique".
                entries[index] = entry with { SkipReason = DedupSkipReason.ExceedsBudget };
                boundsReached = true;
                continue;
            }

            attemptedFiles++;
            attemptedBytes += entry.Length;
            planned.Add(entry with { ReadState = DedupItemReadState.SkippedByBudget });
        }

        scanBoundsReached = boundsReached;
        return planned;
    }

    public static List<DedupAnalysisEntry> Apply(
        List<DedupAnalysisEntry> entries,
        List<DedupAnalysisEntry> planned,
        IReadOnlyList<ContentReadResult> reads,
        IReadOnlyDictionary<long, int> buckets)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(reads);
        var resolved = new Dictionary<string, DedupAnalysisEntry>(StringComparer.Ordinal);
        for (var index = 0; index < planned.Count; index++)
        {
            var entry = planned[index];
            var read = reads[index];
            if (!string.Equals(read.RelativePath.Value, entry.RelativePath.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The content reader must answer its requests in the order they were submitted.");
            }

            resolved[entry.SourceIdentity] = read.Failure == DedupReadFailure.None && read.Sha256 is not null
                ? entry with
                {
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
                    ReadState = DedupItemReadState.ReadFailed,
                    Failure = read.Failure,
                };
        }

        var result = new List<DedupAnalysisEntry>(entries.Count);
        foreach (var entry in entries)
        {
            result.Add(resolved.TryGetValue(entry.SourceIdentity, out var read)
                ? read
                : entry with
                {
                    ReadState = entry.ReadState == DedupItemReadState.NotRead
                        && entry.SkipReason != DedupSkipReason.None
                            ? DedupItemReadState.SkippedByBudget
                            : entry.ReadState,
                    SkipReason = entry.SkipReason == DedupSkipReason.None
                        ? DedupAnalysisPolicy.SkipReasonFor(entry, buckets)
                        : entry.SkipReason,
                });
        }

        return result;
    }

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
