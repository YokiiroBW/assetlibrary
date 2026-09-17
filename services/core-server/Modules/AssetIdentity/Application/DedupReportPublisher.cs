using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Files a finished plan as the current report version of its job, together with the ceilings the job
/// was accepted under. It decides nothing about success: the caller must already hold the lease
/// fence, and this type only records evidence under that fence.
/// </summary>
internal sealed class DedupReportPublisher(DedupReportRegistry reports)
{
    public DedupReportKey Publish(
        Guid taskId,
        DedupAnalysisLimits limits,
        DedupResolvedSource source,
        DedupCurationPlan plan) =>
        reports.Publish(DedupReportComposer.Compose(taskId, limits, source, plan));

    /// <summary>Drops the retained report of a job whose attempt was abandoned or superseded.</summary>
    public void Discard(Guid taskId) => reports.DiscardTask(taskId);
}

/// <summary>
/// Turns a finished plan into the report body that gets filed. It is separate from the publisher because
/// two callers need the body before they can file it: a fresh analysis files it outright, while a recheck
/// must hand it to a compare-and-file step so a version that moved on is refused instead of overwritten.
/// </summary>
internal static class DedupReportComposer
{
    public static DedupReport Compose(
        Guid taskId,
        DedupAnalysisLimits limits,
        DedupResolvedSource source,
        DedupCurationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        var projection = DedupFindingProjection.Project(
            plan,
            DedupSourceRoots.Of(plan),
            DedupJobContractText.MaximumRetainedItemsPerReport);
        return new DedupReport
        {
            AnalysisId = plan.AnalysisId,
            TaskId = taskId,
            LibraryId = source.LibraryId,
            LibraryDisplayName = source.DisplayName,
            AnalyzedAt = plan.AnalyzedAt,
            PolicyVersion = plan.PolicyVersion,
            AcceptedSources = plan.AcceptedSources,
            RejectedSources = plan.RejectedSources,
            AcceptedLibraryIds = [source.LibraryId.Value.ToString("D")],
            Groups = projection.Duplicates,
            Facts = projection.Facts,
            Unverified = projection.Unverified,
            Unreadable = projection.Unreadable,
            Statistics = plan.Statistics,
            Summary = plan.Summary,
            PlanDigest = plan.PlanDigest,
            Limits = limits,
            Truncated = projection.Truncated,
            RetentionBoundary = DedupJobContractText.RetentionBoundary,
        };
    }
}

/// <summary>
/// Maps each accepted source root onto itself, so a page shows the path the composition root resolved
/// rather than one reassembled from an item identity. It exists as a named step because a silent
/// fallback here would show a path the analysis never read.
/// </summary>
internal static class DedupSourceRoots
{
    public static Dictionary<string, string> Of(DedupCurationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in plan.AcceptedSources)
        {
            roots[source.Root] = source.Root;
        }

        return roots;
    }
}
