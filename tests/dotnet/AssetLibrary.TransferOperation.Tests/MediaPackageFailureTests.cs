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
    public void OfflineLibraryAvailabilityIsRefusedAsTargetUnavailable()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        composition.Scope.Grant(
            sandbox,
            revision: "scope-rev-offline",
            availability: AssetLibrary.Modules.LibraryStorage.Contracts.StorageAvailability.Offline);
        var before = sandbox.SnapshotTree();

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);

        // An offline permission is an availability refusal, not a revision change: the two must never be
        // reported with the same code, and an unavailable target must never be guessed as usable.
        Assert.AreEqual("target_unavailable", report.Issues.Single().Code);
        Assert.IsFalse(Codes(report).Contains("scope_changed", StringComparer.Ordinal));
        Assert.AreEqual(0, report.VerifiedFileCount);
        CollectionAssert.AreEqual(before.ToArray(), sandbox.SnapshotTree().ToArray());
    }

    [TestMethod]
    public void ExpiredScopeIsRefusedAsTargetUnavailable()
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

        // An expired permission is the same availability refusal as an offline one, and it is decided
        // before any directory or file is touched.
        Assert.AreEqual("target_unavailable", report.Issues.Single().Code);
        Assert.IsFalse(Codes(report).Contains("scope_changed", StringComparer.Ordinal));
        Assert.AreEqual(0, report.VerifiedFileCount);
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

    [TestMethod]
    public void MissingTrustedTargetRootIsUnavailableRatherThanMissing()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();

        // The trusted target root is gone: the target cannot be observed at all, which is an
        // availability refusal, never an absence of the package and never a conflict.
        Directory.Delete(sandbox.LibraryRoot);
        var before = sandbox.SnapshotTree();

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("target_unavailable", report.Issues.Single().Code);
        Assert.IsFalse(Codes(report).Contains("source_missing", StringComparer.Ordinal));
        Assert.AreEqual(0, report.VerifiedFileCount);
        CollectionAssert.AreEqual(before.ToArray(), sandbox.SnapshotTree().ToArray());
    }

    [TestMethod]
    public void UnobservableTargetPathIsRefusedRatherThanReadAsAbsent()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();

        // The trusted target root is replaced by a file, so the target name cannot even be resolved.
        // A refusal must never be read as "the target does not exist yet".
        Directory.Delete(sandbox.LibraryRoot, recursive: true);
        File.WriteAllText(sandbox.LibraryRoot, "not a directory");

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual(0, report.VerifiedFileCount);
        Assert.IsTrue(
            Codes(report).Any(code => code is "unsafe_path" or "io_failure" or "target_unavailable"),
            Describe(report));
        Assert.IsFalse(Codes(report).Contains("target_exists", StringComparer.Ordinal));
    }

    [TestMethod]
    public void MissingPackageRootIsSourceMissingRatherThanTargetUnavailable()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = MediaPackageSandbox.ReadExamples().Single(item => item.Name == "single");
        using var composition = sandbox.Compose();

        // The package is never written into the staging root: the declared staging_ref is absent, which
        // is a missing source and must not be reported as an unavailable target.
        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        CollectionAssert.Contains(Codes(report), "source_missing");
        Assert.IsFalse(Codes(report).Contains("target_unavailable", StringComparer.Ordinal));
        Assert.AreEqual(0, report.VerifiedFileCount);
    }

    [TestMethod]
    public void RealReadBudgetExactlyCoveringThePackageIsEnough()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        var videoBytes = (long)example.Payloads["video.mp4"].Length;
        using var composition = sandbox.Compose(new MediaPackageInspectionLimits(maximumReadBytes: videoBytes));

        // Every earlier file is read first, so only the video's own length remains: a budget that is
        // exactly enough is enough, and the verdict is a real read rather than a guess.
        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Inspected, report.Status, Describe(report));
        Assert.AreEqual(example.Payloads.Values.Sum(payload => (long)payload.Length), report.VerifiedBytes);
    }

    [TestMethod]
    public void RealReadBudgetOneByteShortIsRefusedAsBudgetExceeded()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        var videoBytes = (long)example.Payloads["video.mp4"].Length;
        using var composition = sandbox.Compose(new MediaPackageInspectionLimits(maximumReadBytes: videoBytes - 1));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);

        // A budget refusal is a budget verdict: it must not be reported as an environment failure.
        Assert.AreEqual("budget_exceeded", report.Issues.Single().Code);
        Assert.IsLessThanOrEqualTo(videoBytes - 1, report.VerifiedBytes);
    }

    [TestMethod]
    public void EnumerationBudgetIsEnforcedWhileWalking()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose(new MediaPackageInspectionLimits(maximumIssues: 1));
        var packageRoot = sandbox.PackagePath(example);
        for (var index = 0; index < MediaPackageInspectionLimits.MaximumEnumeratedEntries; index++)
        {
            File.WriteAllText(Path.Combine(packageRoot, $"filler-{index}.txt"), "unexpected");
        }

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        CollectionAssert.Contains(Codes(report), "budget_exceeded");
        Assert.AreEqual(0, report.VerifiedFileCount);
        Assert.HasCount(1, report.Issues);
        Assert.IsTrue(report.IssuesTruncated, "A capped walk must report truncation.");
    }

    [TestMethod]
    public void TargetHeadroomIsAFloorThatCannotBeLowered()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new MediaPackageInspectionLimits(minimumTargetHeadroomBytes: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new MediaPackageInspectionLimits(minimumTargetHeadroomBytes: -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new MediaPackageInspectionLimits(
                minimumTargetHeadroomBytes: MediaPackageInspectionLimits.MinimumTargetHeadroomBytes - 1));

        // A trusted constructor may only raise the floor, never relax it.
        var raised = new MediaPackageInspectionLimits(
            minimumTargetHeadroomBytes: MediaPackageInspectionLimits.MinimumTargetHeadroomBytes * 2);
        Assert.AreEqual(
            MediaPackageInspectionLimits.MinimumTargetHeadroomBytes * 2,
            raised.MinimumTargetHeadroomByteCount);
    }

    [TestMethod]
    public void HeadroomIsRequiredOnTopOfTheDeclaredPayload()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        var declared = example.Payloads.Values.Sum(payload => (long)payload.Length);
        composition.Space.AvailableBytes = declared + MediaPackageInspectionLimits.MinimumTargetHeadroomBytes - 1;

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.AreEqual("insufficient_space", report.Issues.Single().Code);
        Assert.AreEqual(0, report.VerifiedFileCount);
    }

    [TestMethod]
    public void ReportLocationsAreValidatedRelativePathsOrFixedFieldPositions()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "multipart");
        using var composition = sandbox.Compose();
        var packageRoot = sandbox.PackagePath(example);
        File.Delete(Path.Combine(packageRoot, "poster.png"));
        File.WriteAllText(Path.Combine(packageRoot, "extra.txt"), "unexpected");

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        Assert.IsNotEmpty(report.Issues);
        foreach (var issue in report.Issues)
        {
            var location = issue.Location;
            if (location is null)
            {
                continue;
            }

            Assert.IsFalse(location.Contains(sandbox.Root, StringComparison.OrdinalIgnoreCase), location);
            Assert.DoesNotContain(":\\", location, StringComparison.Ordinal);
            Assert.DoesNotStartWith("/", location);
            Assert.DoesNotContain('\n', location);
            Assert.DoesNotContain('\r', location);
            Assert.AreEqual(location, location.Trim(), location);
        }
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
