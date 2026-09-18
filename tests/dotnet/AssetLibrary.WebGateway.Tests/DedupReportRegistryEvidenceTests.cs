using AssetLibrary.Modules.AssetIdentity.Dedup.Application;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// Pins what the registry's two compare-and-write steps answer, because their callers read one flag for
/// two different questions. <see cref="DedupReportRegistry.FileIfCurrent"/> answers "did I file a new
/// version", and <see cref="DedupReportRegistry.StoreEvidenceIfCurrent"/> deliberately answers "no" while
/// still having done its work: it attaches evidence to a version that already exists rather than creating
/// one. The caller must tell those apart, so both answers are asserted here by name.
/// </summary>
[TestClass]
public sealed class DedupReportRegistryEvidenceTests
{
    /// <summary>
    /// The path that files no new version. Its outcome is not the <c>Superseded</c> answer even though
    /// <c>Filed</c> is false: it names the version it wrote against, which is what tells the two apart.
    /// </summary>
    [TestMethod]
    public async Task EvidenceAgainstTheCurrentVersionIsStoredAndNamesThatVersion()
    {
        using var scenario = await DedupRecheckSyntheticLibrary.CreateAsync();
        var registry = scenario.Registry;
        var target = scenario.Target;
        var library = scenario.Source.LibraryId.Value;
        Assert.IsTrue(
            registry.TryLatest(library, out var current, out _),
            "The fixture must have published a report for this library.");
        var evidence = Evidence(target.PlanDigest);

        var outcome = registry.StoreEvidenceIfCurrent(library, current, target.PlanDigest, evidence);

        Assert.IsFalse(outcome.Filed, "Storing evidence files no new version, and says so.");
        Assert.AreNotEqual(default, outcome.Key, "The outcome must still name the version it wrote against.");
        Assert.AreEqual(current, outcome.Key, "The evidence lands on the version that was asked about.");
        Assert.IsTrue(
            registry.TryLatest(library, out var after, out _),
            "The library must still have a current version.");
        Assert.AreEqual(current, after, "The current version must not move when only evidence is stored.");
        Assert.AreEqual(evidence, registry.EvidenceOf(current), "The evidence must really be readable afterwards.");
    }

    /// <summary>
    /// The path that is refused. A version the library has moved on from must not have evidence attached to
    /// it — and must not be reported as evidence stored either.
    /// </summary>
    [TestMethod]
    public async Task EvidenceAgainstAVersionTheLibraryMovedOnFromIsRefused()
    {
        using var scenario = await DedupRecheckSyntheticLibrary.CreateAsync();
        var registry = scenario.Registry;
        var library = scenario.Source.LibraryId.Value;
        Assert.IsTrue(registry.TryLatest(library, out var verified, out _), "The fixture must publish a report.");
        // A newer analysis of the same library becomes the current one while the older version stays
        // readable: retention keeps it, so "my key still resolves" cannot be the check.
        await scenario.PublishNewerAsync();

        var outcome = registry.StoreEvidenceIfCurrent(
            library,
            verified,
            scenario.Target.PlanDigest,
            Evidence(scenario.Target.PlanDigest));

        Assert.IsFalse(outcome.Filed, "A superseded version must not be filed against.");
        Assert.AreEqual(default, outcome.Key, "A refusal names no version.");
        Assert.IsNull(registry.EvidenceOf(verified), "No evidence may be attached to a superseded version.");
    }

    private static DedupRecheckEvidence Evidence(string digest) =>
        new(true, "Identical", [], 0, 0, 0, DateTimeOffset.UnixEpoch, digest);
}
