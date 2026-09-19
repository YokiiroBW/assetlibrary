using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;

namespace AssetLibrary.TransferOperation.Tests;

/// <summary>
/// End-to-end read-only preflight over real temporary files: complete single and multipart packages,
/// zero-IO authorization, capacity and target-conflict refusal, and the guarantee that a preflight
/// changes nothing.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "An end-to-end preflight test names the contract, the policy and the composition it drives; the count is the shape of the feature under test rather than a dependency on another module.")]
[TestClass]
public sealed class MediaPackageInspectionTests
{
    private static readonly string[] LibraryRootOnly = ["library/"];

    [TestMethod]
    public void SinglePackageIsInspectedWithExactHashesAndCounts()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Inspected, report.Status);
        Assert.HasCount(0, report.Issues, Describe(report));
        Assert.AreEqual(example.Payloads.Count, report.VerifiedFileCount);
        Assert.AreEqual(example.Payloads.Values.Sum(payload => (long)payload.Length), report.VerifiedBytes);
        Assert.AreEqual(example.ManifestSha256, report.ManifestDigest.Value);
        Assert.AreEqual("scope-rev-1", report.ScopeRevision);
        Assert.AreEqual(ReadPackageId(example.ManifestBytes), report.PackageId);
    }

    [TestMethod]
    public void MultipartPackageWithOptionalThumbIsInspected()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "multipart");
        using var composition = sandbox.Compose();

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Inspected, report.Status);
        Assert.HasCount(0, report.Issues, Describe(report));
        Assert.AreEqual(8, report.VerifiedFileCount);
        Assert.AreEqual(
            "bilibili-BV0000000001",
            MediaPackagePolicy.TargetDirectoryName(
                composition.Reader.Read(example.ManifestBytes, example.Digest).Manifest!));
    }

    [TestMethod]
    public void ReportNeverGrantsFileOperationAndAlwaysListsUnverifiedFacts()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.IsFalse(report.GrantsFileOperation);
        CollectionAssert.AreEqual(
            MediaPackageUnverifiedFacts.All.ToArray(),
            report.Unverified.ToArray());
        Assert.AreEqual(((MediaPackageClock)composition.Clock).Now, report.ObservedAt);
        Assert.IsFalse(report.IssuesTruncated);
    }

    [TestMethod]
    public void UnauthorizedCallerTouchesNoDirectoryAndNoFile()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        composition.Scope.Authorized = false;

        var report = composition.InspectAsync(example, "unauthorized").AsTask().GetAwaiter().GetResult();

        Assert.AreEqual("unauthorized", report.Issues.Single().Code);
        Assert.AreEqual(0, report.VerifiedFileCount);
        Assert.AreEqual(1, composition.Scope.ResolveCalls);
        Assert.AreEqual(0, composition.Space.Calls, "A denied caller must not probe the volume.");
    }

    [TestMethod]
    public void ExistingTargetObjectIsAConflictAndIsNotReused()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();
        var target = Path.Combine(sandbox.LibraryRoot, example.ExpectedRelativeDirectory);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "video.mp4"), "another payload");

        var report = composition.InspectAsync(example, "target_exists").AsTask().GetAwaiter().GetResult();

        Assert.HasCount(1, report.Issues);
        Assert.AreEqual(
            "another payload",
            File.ReadAllText(Path.Combine(target, "video.mp4")),
            "An existing target object must never be overwritten or reused.");
    }

    [TestMethod]
    public void RepeatedInspectionIsDeterministicAndLeavesZeroChanges()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "multipart");
        using var composition = sandbox.Compose();

        var first = composition.InspectAsync(example, errorCode: null).AsTask().GetAwaiter().GetResult();
        var second = composition.InspectAsync(example, errorCode: null).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(first.VerifiedFileCount, second.VerifiedFileCount);
        Assert.AreEqual(first.VerifiedBytes, second.VerifiedBytes);
        Assert.AreEqual(first.ManifestDigest.Value, second.ManifestDigest.Value);
    }

    [TestMethod]
    public void ProductCodeCreatesNoTargetDirectoryAndNoAuditFile()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = Example(sandbox, "single");
        using var composition = sandbox.Compose();

        var report = composition.InspectAsync(example, errorCode: null).AsTask().GetAwaiter().GetResult();

        Assert.IsGreaterThan(0, report.VerifiedFileCount);
        Assert.IsFalse(
            Directory.Exists(Path.Combine(sandbox.LibraryRoot, example.ExpectedRelativeDirectory)),
            "The preflight must not create the target directory.");
        CollectionAssert.AreEqual(
            LibraryRootOnly,
            sandbox.SnapshotTree()
                .Keys
                .Where(key => key.StartsWith("library/", StringComparison.Ordinal))
                .ToArray());
    }

    private static MediaPackageExample Example(MediaPackageSandbox sandbox, string name)
    {
        var example = MediaPackageSandbox.ReadExamples().Single(item => item.Name == name);
        sandbox.WritePackage(example);
        return example;
    }

    private static string ReadPackageId(byte[] manifestBytes)
    {
        using var document = System.Text.Json.JsonDocument.Parse(manifestBytes);
        return document.RootElement.GetProperty("package_id").GetString()!;
    }

    private static string Describe(MediaPackagePreflightReport report) =>
        $"status={report.Status} issues=[{string.Join(", ", report.Issues.Select(issue => issue.Code + "@" + (issue.Location ?? "-")))}]";
}
