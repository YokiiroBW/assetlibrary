using System.Text;
using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

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

        // An offline storage root is an availability refusal, not a revision change: the two must never be
        // reported with the same code, and an unavailable target must never be guessed as usable.
        Assert.AreEqual("target_unavailable", report.Issues.Single().Code);
        Assert.IsFalse(Codes(report).Contains("scope_changed", StringComparer.Ordinal));
        Assert.AreEqual(0, report.VerifiedFileCount);
        CollectionAssert.AreEqual(before.ToArray(), sandbox.SnapshotTree().ToArray());
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

        // A lapsed permission is a scope change, not a storage availability refusal: the storage may be
        // perfectly reachable while the caller's right to read it has expired. It is decided before any
        // directory or file is touched, and it must never be reported as an unavailable target.
        Assert.AreEqual("scope_changed", report.Issues.Single().Code);
        Assert.IsFalse(Codes(report).Contains("target_unavailable", StringComparer.Ordinal));
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

        // The awaitable surfaces the cancellation as a TaskCanceledException, which is an
        // OperationCanceledException. The requirement is that cancellation propagates as cancellation
        // instead of becoming a package verdict, so the assertion accepts the base type: demanding the
        // exact base class would fail on the framework's own derived type, not on the product's behaviour.
        var cancelled = Assert.Throws<OperationCanceledException>(
            () => composition.InspectAsync(example, source.Token).AsTask().GetAwaiter().GetResult());
        Assert.IsTrue(cancelled.CancellationToken.IsCancellationRequested);
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

        // The manifest byte budget is decided before authorization, so no scope is ever resolved for a
        // manifest that is already over budget: asking the scope port would be work the contract forbids.
        Assert.AreEqual(0, composition.Scope.ResolveCalls);
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
    public void IssueSinkKeepsEveryInternalStoreBoundedAtTheCap()
    {
        // A hostile manifest can name ten thousand distinct locations. The report is bounded by the cap, and
        // so must the bookkeeping be: the sink may only remember a pair it actually reported, because
        // remembering a refused pair would let a hostile input grow the dedup store without growing the
        // report. The internal store is inspected read-only, since counting only the public Issues would not
        // prove the memory bound.
        var sink = new MediaPackageIssueSink(1);

        Assert.IsTrue(sink.Record("invalid_identity", "files[0]"));
        Assert.IsFalse(sink.Record("invalid_identity", "files[0]"), "A repeated pair is not a new diagnostic.");
        Assert.HasCount(1, sink.Issues);
        Assert.AreEqual(1, InternalStoreCount(sink, "issues"));
        Assert.AreEqual(1, InternalStoreCount(sink, "recorded"));
        Assert.IsFalse(sink.IsTruncated, "Repeating an already-reported pair discards nothing.");

        // A second, genuinely different pair arrives with no room left: it is refused and only marks the
        // truncation. Nothing is stored, so both internal stores stay at the cap.
        Assert.IsFalse(sink.Record("invalid_path", "files[1]"));
        Assert.IsTrue(sink.IsTruncated);
        Assert.HasCount(1, sink.Issues);
        Assert.AreEqual(1, InternalStoreCount(sink, "issues"));
        Assert.AreEqual(1, InternalStoreCount(sink, "recorded"));
        Assert.HasCount(1, sink.Codes);

        // Ten thousand distinct locations must not grow either store beyond the cap.
        for (var index = 0; index < 10_000; index++)
        {
            sink.Record("io_failure", "files[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");
        }

        Assert.HasCount(1, sink.Issues);
        Assert.AreEqual(1, InternalStoreCount(sink, "issues"));
        Assert.AreEqual(1, InternalStoreCount(sink, "recorded"));
        Assert.HasCount(1, sink.Codes);
        Assert.IsTrue(sink.IsTruncated);
    }

    /// <summary>
    /// Counts one of the sink's private stores without changing it, so the memory bound is asserted on the
    /// real internal collection rather than inferred from the public view.
    /// </summary>
    private static int InternalStoreCount(MediaPackageIssueSink sink, string fieldName)
    {
        var field = typeof(MediaPackageIssueSink).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
        Assert.IsNotNull(field, $"The sink must still hold an internal '{fieldName}' store.");
        var value = field.GetValue(sink);
        Assert.IsNotNull(value, $"The internal '{fieldName}' store must be initialized.");
        var count = value.GetType().GetProperty("Count");
        Assert.IsNotNull(count, $"The internal '{fieldName}' store must expose its count.");
        return (int)count.GetValue(value)!;
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
    public void TargetRootReplacedByAFileAfterTheFirstHashIsRefused()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");

        // The root is a real directory when the run starts and stays one while the first file is read; only
        // then is it replaced by a regular file. The first check cannot see this, so the final re-check of the
        // trusted root is what has to refuse it: the run must not report a successful inspection over a
        // target root that stopped being a directory, and it must not report the package as merely existing.
        //
        // Three counters are kept apart on purpose. hashCalls counts every real hash the run performs, which is
        // a property of the package under test and NOT of this scenario, so it is never asserted to a fixed
        // number. flipCount and flipAfterHashCall describe what this test actually did: the root was swapped
        // exactly once, and it was swapped after the first real hash had returned. Asserting hashCalls == 1
        // would conflate the two -- the callback fires after every hash, so the counter reaches the number of
        // files read while only the first callback flips anything.
        var hashCalls = 0;
        var flipCount = 0;
        var flipAfterHashCall = 0;
        using var composition = sandbox.ComposeWithHasher(
            inner => new FlippingFileHasher(
                inner,
                () =>
                {
                    hashCalls++;
                    if (hashCalls != 1)
                    {
                        // Later hashes still run against the real hasher; the root has already been swapped, so
                        // they observe the new state and must not swap it again.
                        return;
                    }

                    // Destructive step: only this test's own sandbox root, proven to be the root under test.
                    Assert.IsTrue(
                        Path.IsPathFullyQualified(sandbox.LibraryRoot),
                        "The replaced root must be an absolute path inside the sandbox.");
                    Assert.IsTrue(
                        sandbox.LibraryRoot.StartsWith(sandbox.Root, StringComparison.OrdinalIgnoreCase),
                        "The replaced root must be inside this test's own sandbox.");
                    Directory.Delete(sandbox.LibraryRoot, recursive: true);
                    File.WriteAllText(sandbox.LibraryRoot, "not a directory");

                    // Recorded only after the swap really happened, so these counters cannot claim a flip that
                    // never occurred.
                    flipCount++;
                    flipAfterHashCall = hashCalls;
                }));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(1, flipCount, "The root must be replaced exactly once.");
        Assert.AreEqual(
            1,
            flipAfterHashCall,
            "The replacement must follow the FIRST real hash, not happen before the run or after a later read.");
        Assert.IsGreaterThanOrEqualTo(
            flipAfterHashCall,
            hashCalls,
            "The flip must be recorded after a hash that really returned.");
        Assert.IsGreaterThan(
            0,
            hashCalls,
            "The scenario is only meaningful if the real hasher was actually called.");
        Assert.IsTrue(File.Exists(sandbox.LibraryRoot), "The fixture must have left a regular file at the root.");
        Assert.IsFalse(Directory.Exists(sandbox.LibraryRoot), "The root must not be a directory any more.");
        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        CollectionAssert.Contains(Codes(report), "unsafe_path", Describe(report));
        Assert.IsFalse(
            Codes(report).Contains("target_exists", StringComparer.Ordinal),
            "The run must not claim the package exists when the root itself is unusable: " + Describe(report));
        Assert.AreEqual(0, report.VerifiedFileCount);
        foreach (var issue in report.Issues)
        {
            var location = issue.Location;
            if (location is null)
            {
                continue;
            }

            Assert.IsFalse(location.Contains(sandbox.Root, StringComparison.OrdinalIgnoreCase), location);
            Assert.DoesNotContain(":\\", location, StringComparison.Ordinal);
            Assert.IsFalse(Path.IsPathFullyQualified(location), location);
        }
    }

    /// <summary>
    /// Delegates every hash to the real streaming hasher and invokes <paramref name="afterEveryHash"/>
    /// immediately after each real hash returns, so a test can change the tree between two real reads. The
    /// callback runs once per hash, not once per run; deciding what to do on which call is the test's job, so
    /// the wrapper stays a plain decorator with no notion of a "first" call.
    /// </summary>
    private sealed class FlippingFileHasher(IMediaPackageFileHasher inner, Action afterEveryHash)
        : IMediaPackageFileHasher
    {
        public async ValueTask<PayloadFacts> HashAsync(
            string absolutePath,
            long byteLimit,
            CancellationToken cancellationToken)
        {
            var facts = await inner.HashAsync(absolutePath, byteLimit, cancellationToken).ConfigureAwait(false);
            afterEveryHash();
            return facts;
        }
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

        // The budget covers every declared payload, not just the last file read: the budget is consumed
        // across the whole package, so a budget of the package's total is exactly enough.
        var packageBytes = example.Payloads.Values.Sum(payload => (long)payload.Length);
        using var composition = sandbox.Compose(new MediaPackageInspectionLimits(maximumReadBytes: packageBytes));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Inspected, report.Status, Describe(report));
        Assert.AreEqual(packageBytes, report.VerifiedBytes);
    }

    [TestMethod]
    public void RealReadBudgetOneByteShortIsRefusedAsBudgetExceeded()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        var packageBytes = example.Payloads.Values.Sum(payload => (long)payload.Length);
        using var composition =
            sandbox.Compose(new MediaPackageInspectionLimits(maximumReadBytes: packageBytes - 1));

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);

        // A budget refusal is a budget verdict: it must not be reported as an environment failure.
        Assert.AreEqual("budget_exceeded", report.Issues.Single().Code);
        Assert.IsLessThanOrEqualTo(packageBytes - 1, report.VerifiedBytes);
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

        // The walk stopped on the entry budget, which is one verdict, not a dropped diagnostic: the report
        // holds that single issue and IsTruncated stays false, because nothing was discarded to make room.
        // The diagnostic cap is proven separately by the multi-issue truncation test.
        Assert.HasCount(1, report.Issues);
        Assert.IsFalse(
            report.IssuesTruncated,
            "An enumeration budget stop is not a truncated diagnostic list.");
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
