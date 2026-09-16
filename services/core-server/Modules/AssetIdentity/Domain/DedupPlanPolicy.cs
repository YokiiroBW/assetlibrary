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
            // Every recorded observation is compared, including files the earlier preview never read
            // (a unique length, an excluded name or an exhausted budget). Those carry no content
            // guarantee, but their path, length and write time were recorded and must still match,
            // otherwise the stored preview no longer describes these sources.
            if (!observed.TryGetValue(item.SourceIdentity, out var fresh))
            {
                disappeared.Add(item.SourceIdentity);
                continue;
            }

            Compare(item, fresh, reasons, changed, unreadable);
        }

        var newItems = current.Items
            .Where(item => !previous.ContainsKey(item.SourceIdentity))
            .Select(item => item.SourceIdentity)
            .ToArray();
        if (newItems.Length > 0)
        {
            reasons.Add(DedupRecountReason.NewFileObserved);
        }

        var status = SelectStatus(plan, current, reasons, changed, disappeared, unreadable);
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

    /// <summary>
    /// Compares one recorded item against the same item as the fresh read observed it. Content is
    /// only compared when both sides actually hold content evidence; otherwise the metadata that was
    /// recorded decides, and the result says the content is still unverified.
    /// </summary>
    private static void Compare(
        DedupPlanItem previous,
        DedupPlanItem fresh,
        SortedSet<DedupRecountReason> reasons,
        SortedSet<string> changed,
        SortedSet<string> unreadable)
    {
        if (fresh.State == DedupPlanItemState.Unreadable)
        {
            unreadable.Add(previous.SourceIdentity);
            reasons.Add(ToReason(fresh.Failure));
            return;
        }

        if (fresh.State == DedupPlanItemState.NotAnalyzed)
        {
            // Still present, but the fresh read spent no budget on it. Nothing about its content is
            // claimed, and the recorded metadata decides whether the stored observation still holds.
            if (!string.Equals(BuildMetadataKey(previous), BuildMetadataKey(fresh), StringComparison.Ordinal))
            {
                reasons.Add(DedupRecountReason.MetadataChanged);
                changed.Add(previous.SourceIdentity);
            }
            else
            {
                reasons.Add(DedupRecountReason.ContentUnverified);
            }

            return;
        }

        if (previous.State == DedupPlanItemState.Unreadable)
        {
            // The file is readable now although it was not before: new evidence, not a stale plan.
            reasons.Add(DedupRecountReason.ContentChanged);
            changed.Add(previous.SourceIdentity);
            return;
        }

        if (previous.State == DedupPlanItemState.NotAnalyzed)
        {
            // Content that was never verified cannot be compared, so the plan must not be called
            // current. Any recorded metadata difference is a change; otherwise it is simply still
            // unverifiable against the earlier observation.
            if (!string.Equals(BuildMetadataKey(previous), BuildMetadataKey(fresh), StringComparison.Ordinal))
            {
                reasons.Add(DedupRecountReason.MetadataChanged);
                changed.Add(previous.SourceIdentity);
            }
            else
            {
                reasons.Add(DedupRecountReason.ContentUnverified);
            }

            return;
        }

        DetectChange(previous, fresh, reasons, changed);
    }

    private static void DetectChange(
        DedupPlanItem previous,
        DedupPlanItem fresh,
        SortedSet<DedupRecountReason> reasons,
        SortedSet<string> changed)
    {
        if (string.Equals(BuildContentKey(previous), BuildContentKey(fresh), StringComparison.Ordinal))
        {
            return;
        }

        var contentChanged = !string.Equals(previous.Sha256, fresh.Sha256, StringComparison.Ordinal)
            || previous.StructureHash != fresh.StructureHash;
        reasons.Add(contentChanged
            ? DedupRecountReason.ContentChanged
            : DedupRecountReason.MetadataChanged);
        changed.Add(previous.SourceIdentity);
    }

    /// <summary>Every content fact this plan recorded for one item.</summary>
    private static string BuildContentKey(DedupPlanItem item) =>
        string.Join(
            '|',
            BuildMetadataKey(item),
            item.Sha256 ?? "unhashed",
            item.StructureHash);

    /// <summary>
    /// The facts that are known even when content was never read. A difference here is a real
    /// difference even for a file this preview could not verify.
    /// </summary>
    private static string BuildMetadataKey(DedupPlanItem item) =>
        string.Join(
            '|',
            item.Length,
            item.LastWriteTimeUtc.UtcTicks,
            item.ReadState.ToString(),
            item.SkipReason.ToString());

    private static DedupRecountReason ToReason(DedupReadFailure failure) => failure switch
    {
        DedupReadFailure.PermissionDenied => DedupRecountReason.PermissionDenied,
        DedupReadFailure.Missing => DedupRecountReason.SourceMissing,
        DedupReadFailure.None => DedupRecountReason.None,
        _ => DedupRecountReason.ScanIncomplete,
    };

    private static DedupRecountStatus SelectStatus(
        DedupCurationPlan plan,
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

        // Unverified content is a statement about what this comparison could not prove, not a
        // difference: a file that was never read and did not change keeps the plan current.
        var differences = reasons.Where(reason => reason != DedupRecountReason.ContentUnverified).ToArray();
        var digestMatches = string.Equals(plan.PlanDigest, current.PlanDigest, StringComparison.Ordinal);
        if (differences.Length > 0 || !digestMatches || !ContentEvidenceStillMatches(plan, current))
        {
            return DedupRecountStatus.SourceChanged;
        }

        return DedupRecountStatus.Identical;
    }

    /// <summary>
    /// True when every item the stored plan recorded a content hash for still carries that same hash
    /// in the fresh read. This is the explicit proof the review asked for: a plan may not be called
    /// current unless the content facts it states are the content facts the sources still have.
    /// </summary>
    private static bool ContentEvidenceStillMatches(DedupCurationPlan plan, DedupCurationPlan current)
    {
        if (plan.Items.Count != current.Items.Count)
        {
            return false;
        }

        var observed = current.Items.ToDictionary(item => item.SourceIdentity, StringComparer.Ordinal);
        foreach (var item in plan.Items)
        {
            if (!observed.TryGetValue(item.SourceIdentity, out var fresh))
            {
                return false;
            }

            if (!string.Equals(BuildContentKey(item), BuildContentKey(fresh), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
