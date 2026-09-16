using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Domain;

/// <summary>
/// Compares a stored preview against a fresh read of the same sources. This is the only way a
/// reviewer can learn that the files behind a plan moved on, and it never repairs or rewrites the
/// earlier plan in place.
/// </summary>
public static class DedupPlanPolicy
{
    public static DedupRecountResult Recount(DedupCurationPlan plan, DedupCurationPlan current)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(current);

        var previous = plan.Items.ToDictionary(item => item.SourceIdentity, StringComparer.Ordinal);
        var observed = current.Items.ToDictionary(item => item.SourceIdentity, StringComparer.Ordinal);
        var reasons = new SortedSet<DedupRecountReason>();
        var changed = new SortedSet<string>(StringComparer.Ordinal);
        var disappeared = new SortedSet<string>(StringComparer.Ordinal);
        var unreadable = new SortedSet<string>(StringComparer.Ordinal);

        if (current.RejectedSources.Count > 0)
        {
            reasons.Add(DedupRecountReason.SourceRejected);
        }

        if (!plan.AcceptedSources
            .Select(source => source.SourceId)
            .SequenceEqual(current.AcceptedSources.Select(source => source.SourceId)))
        {
            reasons.Add(DedupRecountReason.SourceAvailabilityChanged);
        }

        if (!string.Equals(plan.Summary.FailureCode, current.Summary.FailureCode, StringComparison.Ordinal))
        {
            reasons.Add(DedupRecountReason.ScanIncomplete);
        }

        foreach (var item in plan.Items)
        {
            if (item.State == DedupPlanItemState.NotAnalyzed)
            {
                // The earlier preview never read this file, so nothing about it can have changed
                // and nothing about it may be claimed.
                continue;
            }

            if (!observed.TryGetValue(item.SourceIdentity, out var fresh))
            {
                disappeared.Add(item.SourceIdentity);
                continue;
            }

            switch (fresh.State)
            {
                case DedupPlanItemState.Unreadable:
                    unreadable.Add(item.SourceIdentity);
                    reasons.Add(ToReason(fresh.Failure));
                    break;
                case DedupPlanItemState.NotAnalyzed:
                    // Still present but no longer covered by the analysis budget or by discovery.
                    reasons.Add(DedupRecountReason.ScanIncomplete);
                    changed.Add(item.SourceIdentity);
                    break;
                default:
                    DetectChange(item, fresh, reasons, changed);
                    break;
            }
        }

        var newItems = current.Items
            .Where(item => !previous.ContainsKey(item.SourceIdentity))
            .Select(item => item.SourceIdentity)
            .ToArray();
        if (newItems.Length > 0)
        {
            reasons.Add(DedupRecountReason.NewFileObserved);
        }

        var status = SelectStatus(current, reasons, changed, disappeared, unreadable);
        return new DedupRecountResult(
            plan.AnalysisId,
            status,
            [.. reasons],
            [.. changed.Select(ToRelativePath)],
            [.. disappeared.Select(ToRelativePath)],
            [.. newItems.Select(ToRelativePath)],
            plan.PlanDigest,
            string.Equals(plan.PlanDigest, current.PlanDigest, StringComparison.Ordinal)
                ? plan.PlanDigest
                : current.PlanDigest,
            current);
    }

    /// <summary>
    /// A source identity is "canonical root|relative path". Only the relative part is a legal asset
    /// path, so it is the part a caller may pass on to a read-only query.
    /// </summary>
    private static RelativeAssetPath ToRelativePath(string sourceIdentity)
    {
        var separator = sourceIdentity.IndexOf('|', StringComparison.Ordinal);
        return separator < 0
            ? throw new InvalidOperationException("A source identity must carry its canonical root.")
            : new RelativeAssetPath(sourceIdentity[(separator + 1)..]);
    }

    private static void DetectChange(
        DedupPlanItem previous,
        DedupPlanItem fresh,
        SortedSet<DedupRecountReason> reasons,
        SortedSet<string> changed)
    {
        var previousKey = BuildIdentityKey(previous);
        var freshKey = BuildIdentityKey(fresh);
        if (string.Equals(previousKey, freshKey, StringComparison.Ordinal))
        {
            return;
        }

        var contentChanged = previous.Length != fresh.Length
            || !string.Equals(previous.Sha256, fresh.Sha256, StringComparison.Ordinal)
            || previous.StructureHash != fresh.StructureHash;
        reasons.Add(contentChanged
            ? DedupRecountReason.ContentChanged
            : DedupRecountReason.MetadataChanged);
        changed.Add(previous.SourceIdentity);
    }

    private static string BuildIdentityKey(DedupPlanItem item) =>
        string.Join(
            '|',
            item.Length,
            item.Sha256 ?? "none",
            item.StructureHash,
            item.LastWriteTimeUtc.UtcTicks);

    private static DedupRecountReason ToReason(DedupReadFailure failure) => failure switch
    {
        DedupReadFailure.PermissionDenied => DedupRecountReason.PermissionDenied,
        DedupReadFailure.Missing => DedupRecountReason.SourceMissing,
        DedupReadFailure.None => DedupRecountReason.None,
        _ => DedupRecountReason.ScanIncomplete,
    };

    private static DedupRecountStatus SelectStatus(
        DedupCurationPlan current,
        SortedSet<DedupRecountReason> reasons,
        SortedSet<string> changed,
        SortedSet<string> disappeared,
        SortedSet<string> unreadable)
    {
        if (current.Summary.Status is DedupAnalysisStatus.TimedOut or DedupAnalysisStatus.Cancelled)
        {
            return DedupRecountStatus.Timeout;
        }

        if (current.Summary.Status == DedupAnalysisStatus.SourceRejected)
        {
            return DedupRecountStatus.SourceChanged;
        }

        if (unreadable.Count > 0)
        {
            return DedupRecountStatus.UnreadableNow;
        }

        if (disappeared.Count > 0)
        {
            return DedupRecountStatus.Disappeared;
        }

        if (reasons.Contains(DedupRecountReason.SourceAvailabilityChanged))
        {
            return DedupRecountStatus.SourceChanged;
        }

        if (changed.Count > 0)
        {
            return DedupRecountStatus.SourceChanged;
        }

        if (reasons.Contains(DedupRecountReason.NewFileObserved))
        {
            return DedupRecountStatus.NewContent;
        }

        // The plan digest covers every recorded observation, so an identical digest is a stronger
        // statement than the per-item loop alone and is used as the final tie-break.
        return reasons.Count == 0
            ? DedupRecountStatus.Identical
            : DedupRecountStatus.SourceChanged;
    }
}
