using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// The outcomes a recheck files when no scan ran. Each one is its own code because a caller acts on them
/// differently: a version that is gone, a version that was replaced, a report with nothing to verify, and
/// a share that cannot be read now are four different answers, and none of them is "nothing changed".
/// </summary>
internal static class DedupRecheckRefusals
{
    /// <summary>The report version the payload named is no longer retained.</summary>
    public static DedupRecheckRun Missing(DedupJobPayloadReader.RecheckTarget target) => Refuse(
        target,
        string.Empty,
        "要复核的报告版本已不再保留。",
        "dedup_report_not_retained");

    /// <summary>The retained version is no longer the one the caller asked about.</summary>
    public static DedupRecheckRun Superseded(DedupJobPayloadReader.RecheckTarget target, DedupReportKey key) => Refuse(
        target,
        key.VersionText,
        "要复核的报告版本已被更新的版本取代，本次复核未执行。",
        "dedup_version_conflict");

    /// <summary>The report retained no source a recheck may read.</summary>
    public static DedupRecheckRun Rejected(DedupJobPayloadReader.RecheckTarget target, DedupReportKey key) => Refuse(
        target,
        key.VersionText,
        "报告没有可复核的来源范围。",
        "dedup_scope_rejected");

    /// <summary>
    /// No scan ran because a source it would have walked is not readable now. Spending a full pass to
    /// discover a share is gone would hold the attempt for as long as the share takes to fail, so it is
    /// refused up front.
    /// </summary>
    public static DedupRecheckRun Unreachable(DedupJobPayloadReader.RecheckTarget target, DedupReportKey key) => Refuse(
        target,
        key.VersionText,
        "要复核的来源当前不可读取，本次复核未执行。",
        "source_unavailable");

    private static DedupRecheckRun Refuse(
        DedupJobPayloadReader.RecheckTarget target,
        string analysisVersion,
        string reason,
        string failureCode) => new(
        Completed: false,
        Status: DedupRecountStatus.SourceChanged,
        PlanStillCurrent: false,
        Reasons: [reason],
        ChangedPaths: [],
        DisappearedPaths: [],
        NewPaths: [],
        PlanDigest: target.PlanDigest,
        PreviousPlanDigest: null,
        VerifiedGeneration: target.Generation,
        AnalysisVersion: analysisVersion,
        FailureCode: failureCode);
}
