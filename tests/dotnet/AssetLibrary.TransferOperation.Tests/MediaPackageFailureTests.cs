using System.Text;
using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

/// <summary>
/// Named failure behaviour over real files: budget, missing and extra objects, digest and size
/// mismatches, mutation during the run, revocation, cancellation, elapsed budget, concurrency and
/// report bounding.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "A failure-mode test names every frozen code, every budget it lowers, the policy it violates and the composition that runs the real preflight; the count is the shape of the failure surface under test.")]
[TestClass]
public sealed class MediaPackageFailureTests
{
    [TestMethod]
    public void InsufficientTargetSpaceIsRefusedBeforeAnyFileIsRead()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        composition.Space.AvailableBytes =
            example.Payloads.Values.Sum(payload => (long)payload.Length) + 1024;
        var before = sandbox.SnapshotTree();

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("insufficient_space", report.Issues.Single().Code);
        Assert.AreEqual(0, report.VerifiedFileCount);
        CollectionAssert.AreEqual(before.ToArray(), sandbox.SnapshotTree().ToArray());
    }

    [TestMethod]
    public void ExactlyDeclaredBytesPlusHeadroomIsEnough()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        composition.Space.AvailableBytes =
            example.Payloads.Values.Sum(payload => (long)payload.Length)
            + MediaPackageInspectionLimits.MinimumTargetHeadroomBytes;

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Inspected, report.Status, Describe(report));
    }

    [TestMethod]
    public void UnknownTargetSpaceIsRefusedInsteadOfAssumed()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        composition.Space.AvailableBytes = null;

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("target_unavailable", report.Issues.Single().Code);
    }

    [TestMethod]
    public void MissingDeclaredFileIsReportedAsSourceMissing()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        File.Delete(Path.Combine(sandbox.PackagePath(example), "video.mp4"));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        CollectionAssert.Contains(Codes(report), "source_missing");
        Assert.AreEqual(0, report.VerifiedFileCount);
    }

    [TestMethod]
    public void ExtraFileIsReportedAsInvalidFileSet()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        File.WriteAllText(Path.Combine(sandbox.PackagePath(example), "extra.txt"), "unexpected");

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        CollectionAssert.Contains(Codes(report), "invalid_file_set");
        Assert.AreEqual("extra.txt", report.Issues.First(issue => issue.Code == "invalid_file_set").Location);
    }

    [TestMethod]
    public void ExtraEmptyDirectoryIsReportedAsInvalidFileSet()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        Directory.CreateDirectory(Path.Combine(sandbox.PackagePath(example), "Extras"));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("Extras/", report.Issues.First(issue => issue.Code == "invalid_file_set").Location);
    }

    [TestMethod]
    public void DeclaredFileWithDifferentLengthIsReportedAsSizeMismatch()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        var video = Path.Combine(sandbox.PackagePath(example), "video.mp4");
        File.WriteAllBytes(video, Encoding.ASCII.GetBytes("SYNTHETIC-NONPLAYABLE-VIDEO:video.mp4-extended"));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("size_mismatch", report.Issues.Single().Code);
        Assert.AreEqual("video.mp4", report.Issues.Single().Location);
    }

    [TestMethod]
    public void SameLengthButDifferentBytesIsReportedAsHashMismatch()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        var video = Path.Combine(sandbox.PackagePath(example), "video.mp4");
        var original = File.ReadAllBytes(video);
        var mutated = original.ToArray();
        mutated[0] = mutated[0] == (byte)'A' ? (byte)'B' : (byte)'A';
        File.WriteAllBytes(video, mutated);
        Assert.HasCount(original.Length, mutated);

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("hash_mismatch", report.Issues.Single().Code);
    }

    [TestMethod]
    public void ScopeRevokedDuringTheRunIsReportedAsScopeChanged()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "multipart");
        using var composition = sandbox.Compose();
        composition.Scope.RevokeAfterCurrentCalls = 1;

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("scope_changed", report.Issues.Single().Code);
        Assert.AreEqual(0, report.VerifiedFileCount);
    }

    [TestMethod]
    public void OfflineLibraryAvailabilityIsRefusedAsScopeChanged()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        composition.Scope.Grant(
            sandbox,
            revision: "scope-rev-offline",
            availability: AssetLibrary.Modules.LibraryStorage.Contracts.StorageAvailability.Offline);

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("scope_changed", report.Issues.Single().Code);
        Assert.AreEqual(0, report.VerifiedFileCount);
    }

    [TestMethod]
    public void ExpiredScopeIsRefusedAsScopeChanged()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        composition.Scope.Grant(
            sandbox,
            revision: "scope-rev-expired",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("scope_changed", report.Issues.Single().Code);
    }

    [TestMethod]
    public void CallerCancellationPropagatesInsteadOfBecomingAVerdict()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(
            () => composition.InspectAsync(example, source.Token).AsTask().GetAwaiter().GetResult());
    }

    [TestMethod]
    public void ElapsedBudgetIsReportedAsTimeout()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        var composition = sandbox.ComposeExpired();
        var before = sandbox.SnapshotTree();

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("timeout", report.Issues.Single().Code);
        Assert.AreEqual(0, report.VerifiedFileCount);
        CollectionAssert.AreEqual(before.ToArray(), sandbox.SnapshotTree().ToArray());
    }

    [TestMethod]
    public void DeclaredFileCountOverTheInstanceBudgetIsRefused()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "multipart");
        using var composition = sandbox.Compose(new MediaPackageInspectionLimits(maximumFiles: 4));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("budget_exceeded", report.Issues.Single().Code);
        Assert.AreEqual(0, report.VerifiedFileCount);
    }

    [TestMethod]
    public void RealReadBudgetSmallerThanThePackageIsRefused()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose(new MediaPackageInspectionLimits(maximumReadBytes: 64));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        CollectionAssert.Contains(Codes(report), "budget_exceeded");
        Assert.IsLessThanOrEqualTo(64, report.VerifiedBytes, "The read budget must be enforced while streaming.");
    }

    [TestMethod]
    public void ManifestLargerThanTheInstanceBudgetIsRefused()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = MediaPackageSandbox.ReadExamples()[0];
        using var composition = sandbox.Compose(new MediaPackageInspectionLimits(maximumManifestBytes: 32));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("budget_exceeded", report.Issues.Single().Code);
        Assert.AreEqual(1, composition.Scope.ResolveCalls);
    }

    [TestMethod]
    public void ConcurrentInstanceIsBusyAndTheCapacityIsReleasedAfterwards()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        composition.Space.Block = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var gate = Task.Run(
            () => composition.InspectAsync(example).AsTask().GetAwaiter().GetResult());
        Assert.IsTrue(
            composition.Space.Entered.Wait(TimeSpan.FromSeconds(30)),
            "The first preflight never reached the volume observation.");

        var busy = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();
        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, busy.Status);
        Assert.AreEqual("busy", busy.Issues.Single().Code);

        composition.Space.Block.SetResult();
        var first = gate.GetAwaiter().GetResult();
        Assert.AreEqual(MediaPackageInspectionStatus.Inspected, first.Status, Describe(first));

        // The slot is free again, so a subsequent preflight succeeds rather than staying busy.
        var after = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();
        Assert.AreEqual(MediaPackageInspectionStatus.Inspected, after.Status, Describe(after));
    }

    [TestMethod]
    public void IssueReportIsBoundedAndReportsTruncation()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose(new MediaPackageInspectionLimits(maximumIssues: 2));
        var packageRoot = sandbox.PackagePath(example);
        for (var index = 0; index < 8; index++)
        {
            File.WriteAllText(Path.Combine(packageRoot, $"extra-{index}.txt"), "unexpected");
        }

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.HasCount(2, report.Issues);
        Assert.IsTrue(report.IssuesTruncated, "Exceeding the issue cap must be flagged.");
        Assert.IsTrue(
            report.Issues.All(issue => !issue.Location!.Contains(':') && !Path.IsPathRooted(issue.Location)),
            "A report must never carry an absolute path.");
    }

    [TestMethod]
    public void ReportNeverCarriesAbsolutePathsOrFileContent()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "multipart");
        using var composition = sandbox.Compose();
        File.Delete(Path.Combine(sandbox.PackagePath(example), "poster.png"));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.IsNotEmpty(report.Issues);
        var text = string.Join("|", report.Issues.Select(issue => $"{issue.Code}@{issue.Location}"));
        Assert.IsFalse(text.Contains(sandbox.Root, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains(sandbox.StagingRoot, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("SYNTHETIC", StringComparison.Ordinal));
    }

    private static MediaPackageExample Example(MediaPackageSandbox sandbox, string name)
    {
        var example = MediaPackageSandbox.ReadExamples().Single(item => item.Name == name);
        sandbox.WritePackage(example);
        return example;
    }

    private static string[] Codes(MediaPackagePreflightReport report) =>
        [.. report.Issues.Select(issue => issue.Code).Distinct(StringComparer.Ordinal)];

    private static string Describe(MediaPackagePreflightReport report) =>
        $"status={report.Status} issues=[{string.Join(", ", report.Issues.Select(issue => issue.Code + "@" + (issue.Location ?? "-")))}]";
}
