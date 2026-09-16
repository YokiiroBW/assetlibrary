using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Domain;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Read-only duplicate candidate analysis and preview planning. It reuses the shared read-only
/// discovery adapter and a bounded content reader; it owns no writer, no trash integration and no
/// execution right, so nothing in this type can move, copy, rename or delete a source file.
/// </summary>
public sealed class DedupAnalyzer
{
    private readonly DedupAnalysisRunner runner;

    public DedupAnalyzer(
        IDedupFileDiscovery discovery,
        IDedupContentReader contentReader,
        IDedupSourceAvailability availability,
        IDedupSourceScopeQuery scopes,
        IDedupClock clock)
    {
        runner = new DedupAnalysisRunner(discovery, contentReader, availability, scopes, clock);
    }

    /// <summary>Produces the reviewable preview. It never modifies a source file.</summary>
    public Task<DedupCurationPlan> AnalyzeAsync(
        DedupAnalysisRequest request,
        CancellationToken cancellationToken = default) =>
        runner.AnalyzeAsync(request, cancellationToken);

    /// <summary>
    /// Re-reads the sources of a stored preview and reports what changed. It never edits the stored
    /// plan: a stale preview stays stale until a human decides what to do with it.
    /// </summary>
    public async Task<DedupRecountResult> RecountAsync(
        DedupRecountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = await runner.AnalyzeAsync(
            new DedupAnalysisRequest(
                request.Plan.AnalysisId,
                request.Sources,
                DedupRecountLimits.Derive(request.Plan),
                request.Timeout),
            cancellationToken).ConfigureAwait(false);
        return DedupPlanPolicy.Recount(request.Plan, current);
    }
}

/// <summary>
/// The actual read-only pipeline: validate the requested sources, observe them, spend a bounded
/// read budget on real candidates and hand the observations to the preview builder.
/// </summary>
internal sealed class DedupAnalysisRunner
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<AssetRelation>> NoRelations =
        new Dictionary<string, IReadOnlyList<AssetRelation>>(StringComparer.Ordinal);

    private readonly DedupScopeResolver scopeResolver;
    private readonly DedupSourceCollector collector;
    private readonly DedupContentReadBatch batches;
    private readonly DedupPlanBuilder plans;

    public DedupAnalysisRunner(
        IDedupFileDiscovery discovery,
        IDedupContentReader contentReader,
        IDedupSourceAvailability availability,
        IDedupSourceScopeQuery scopes,
        IDedupClock clock)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(contentReader);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(clock);
        scopeResolver = new DedupScopeResolver(scopes, availability);
        collector = new DedupSourceCollector(discovery);
        batches = new DedupContentReadBatch(contentReader);
        plans = new DedupPlanBuilder(clock);
    }

    public async Task<DedupCurationPlan> AnalyzeAsync(
        DedupAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Limits.Validate();
        if (request.Sources.Count == 0)
        {
            throw new ArgumentException("A dedup analysis requires at least one source.", nameof(request));
        }

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A dedup analysis needs a positive timeout.");
        }

        var scope = await scopeResolver
            .ResolveAsync(request.Sources, cancellationToken)
            .ConfigureAwait(false);
        if (!scope.AnyAccepted)
        {
            return plans.Build(
                request,
                scope,
                [],
                [],
                NoRelations,
                [],
                scanBoundsReached: false,
                DedupAnalysisStatus.SourceRejected);
        }

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(request.Timeout);
        var failures = new List<DedupSourceFailure>();
        try
        {
            var entries = await collector
                .CollectAsync(scope.Accepted, request.Limits, failures, bounded.Token)
                .ConfigureAwait(false);
            if (entries.Count == 0)
            {
                return plans.Build(
                    request,
                    scope,
                    entries,
                    [],
                    NoRelations,
                    failures,
                    scanBoundsReached: false,
                    failures.Count > 0
                        ? DedupAnalysisStatus.PartiallyAnalyzed
                        : DedupAnalysisStatus.Empty);
            }

            var buckets = DedupAnalysisPolicy.CountLengthBuckets(entries);
            var planned = DedupReadPlanner.Plan(entries, buckets, request.Limits, out var scanBoundsReached);
            var read = await batches
                .ReadAsync(planned, request.Limits, bounded.Token)
                .ConfigureAwait(false);
            var resolved = DedupReadPlanner.Apply(entries, planned, read, buckets);
            return plans.Build(
                request,
                scope,
                resolved,
                DedupAnalysisPolicy.BuildGroups(resolved),
                DedupAnalysisPolicy.DetectRelations(resolved),
                failures,
                scanBoundsReached,
                DedupReadPlanner.SelectStatus(resolved, failures));
        }
        catch (OperationCanceledException) when (
            bounded.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return plans.Build(
                request,
                scope,
                [],
                [],
                NoRelations,
                failures,
                scanBoundsReached: true,
                DedupAnalysisStatus.TimedOut);
        }
    }
}

/// <summary>
/// A recheck must use the ceilings the preview was produced under, otherwise "unchanged" could mean
/// "not looked at this run".
/// </summary>
internal static class DedupRecountLimits
{
    public static DedupAnalysisLimits Derive(DedupCurationPlan plan) =>
        new(
            Math.Max(plan.Statistics.ObservedEntries, DedupAnalysisLimits.DefaultMaximumFiles),
            Math.Max(plan.Statistics.ReadBytes, DedupAnalysisLimits.DefaultMaximumBytes),
            plan.Items.Count == 0
                ? DedupAnalysisLimits.DefaultMaximumFileBytes
                : (int)Math.Clamp(
                    plan.Items.Max(item => item.Length),
                    1,
                    DedupAnalysisLimits.DefaultMaximumFileBytes));
}
