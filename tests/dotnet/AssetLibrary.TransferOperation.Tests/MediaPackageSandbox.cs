using System.Security.Cryptography;
using System.Text.Json;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Infrastructure;
using AssetLibrary.Modules.TransferSync.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AssetLibrary.TransferOperation.Tests;

/// <summary>
/// Real temporary package tree for one test, created only under
/// <c>.runtime/sandbox-storage/TS-099</c>. It also loads the frozen candidate fixtures committed to this
/// repository under the test project's own <c>Fixtures/MediaPackage</c> directory, so a preflight test
/// reads exactly the bytes that are under version control next to it and never depends on another
/// directory being present in the working tree.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "The sandbox is the single place where a preflight is wired for tests: it names every port, the service, the real inspector, the limits and the fixture records, and that set is the composition itself rather than a dependency on another module.")]
internal sealed class MediaPackageSandbox : IDisposable
{
    /// <summary>
    /// The fixture directory as a path relative to the repository root. It is the complete path, not a
    /// short tail: the same value both locates the repository and resolves the fixtures, so a root can
    /// only be accepted when it really holds this project's committed fixtures.
    /// </summary>
    public const string FixtureRelativeDirectory =
        "tests/dotnet/AssetLibrary.TransferOperation.Tests/Fixtures/MediaPackage";

    public const string MarkerName = ".assetlibrary-ts-099-sandbox";
    public const string MarkerValue = "assetlibrary-ts-099-sandbox-v1";

    private MediaPackageSandbox(string root, string stagingRoot, string libraryRoot)
    {
        Root = root;
        StagingRoot = stagingRoot;
        LibraryRoot = libraryRoot;
    }

    public string Root { get; }

    public string StagingRoot { get; }

    public string LibraryRoot { get; }

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string FixtureRoot { get; } = Path.Combine(
        RepositoryRoot,
        FixtureRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));

    public static MediaPackageSandbox Create()
    {
        var baseDirectory = Path.Combine(
            RepositoryRoot,
            ".runtime",
            "sandbox-storage",
            "TS-099");
        Directory.CreateDirectory(baseDirectory);
        var root = Path.Combine(baseDirectory, $"fixture-{Guid.NewGuid():N}");
        var stagingRoot = Path.Combine(root, "staging");
        var libraryRoot = Path.Combine(root, "library");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(libraryRoot);
        File.WriteAllText(
            Path.Combine(root, MarkerName),
            MarkerValue);
        return new MediaPackageSandbox(root, stagingRoot, libraryRoot);
    }

    /// <summary>
    /// Writes the example's decoded manifest bytes and payload bytes under
    /// <c>staging/&lt;staging_ref&gt;</c> without any transformation.
    /// </summary>
    public string WritePackage(MediaPackageExample example)
    {
        ArgumentNullException.ThrowIfNull(example);
        var stagingRef = ReadStagingRef(example.ManifestBytes);
        var packageRoot = Path.Combine(StagingRoot, stagingRef);
        Directory.CreateDirectory(packageRoot);
        foreach (var (relativePath, payload) in example.Payloads)
        {
            var destination = Path.Combine(
                packageRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, payload);
        }

        return packageRoot;
    }

    public string PackagePath(MediaPackageExample example) =>
        Path.Combine(StagingRoot, ReadStagingRef(example.ManifestBytes));

    public string Resolve(string relativePath) =>
        Path.Combine(StagingRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public MediaPackageStagingRoot StagingToken =>
        new(StagingRoot, RootPathComparison.CaseInsensitive);

    public MediaPackageStagingRoot LibraryToken =>
        new(LibraryRoot, RootPathComparison.CaseInsensitive);

    /// <summary>
    /// Frozen proof that the whole sandbox tree is byte-identical after a preflight, walked
    /// independently of the product code.
    /// </summary>
    public SortedDictionary<string, string> SnapshotTree()
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(Root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    snapshot.Add(Relative(entry) + "/", attributes.HasFlag(FileAttributes.ReparsePoint)
                        ? "directory:reparse"
                        : "directory");
                    if (!attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                snapshot.Add(
                    Relative(entry),
                    $"file:{new FileInfo(entry).Length}:{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(entry)))}");
            }
        }

        return snapshot;
    }

    public string Relative(string absolutePath) =>
        Path.GetRelativePath(Root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Wires the real preflight service over this sandbox: the real strict reader, the real isolated
    /// inspector, the real streaming hasher and the real path boundary, with only the trusted ports
    /// replaced by test doubles.
    /// </summary>
    public MediaPackageComposition Compose(MediaPackageInspectionLimits? limits = null)
    {
        var effective = limits ?? new MediaPackageInspectionLimits();
        return ComposeCore(effective, new MediaPackageClock());
    }

    /// <summary>
    /// Composition whose budget token is already cancelled, so the elapsed-budget verdict is provable
    /// without sleeping.
    /// </summary>
    public MediaPackageComposition ComposeExpired(MediaPackageInspectionLimits? limits = null) =>
        ComposeCore(limits ?? new MediaPackageInspectionLimits(), new MediaPackageExpiredClock());

    private MediaPackageComposition ComposeCore(
        MediaPackageInspectionLimits effective,
        TimeProvider clock)
    {
        var scope = new MediaPackageScopeStub();
        scope.Grant(this);
        var space = new MediaPackageSpaceStub();
        var service = new MediaPackageInspectionService(
            new MediaPackageManifestReader(effective),
            scope,
            new IsolatedMediaPackageInspector(
                scope,
                new MediaPackageFileHasher(effective.StreamBufferByteCount),
                space),
            clock,
            effective);
        return new MediaPackageComposition(this, service, scope, space, clock, effective);
    }

    /// <summary>
    /// Composition whose file hasher is wrapped, so a test can act between two real hash calls without
    /// replacing the port. Everything else, including the real streaming hasher underneath the wrapper, is
    /// the same composition the product runs.
    /// </summary>
    public MediaPackageComposition ComposeWithHasher(
        Func<IMediaPackageFileHasher, IMediaPackageFileHasher> wrap,
        MediaPackageInspectionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        var effective = limits ?? new MediaPackageInspectionLimits();
        var scope = new MediaPackageScopeStub();
        scope.Grant(this);
        var space = new MediaPackageSpaceStub();
        var clock = new MediaPackageClock();
        var service = new MediaPackageInspectionService(
            new MediaPackageManifestReader(effective),
            scope,
            new IsolatedMediaPackageInspector(
                scope,
                wrap(new MediaPackageFileHasher(effective.StreamBufferByteCount)),
                space),
            clock,
            effective);
        return new MediaPackageComposition(this, service, scope, space, clock, effective);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    public static IReadOnlyList<MediaPackageExample> ReadExamples() =>
        ReadExamplesAt(Path.Combine(FixtureRoot, "examples.json"));

    public static IReadOnlyList<MediaPackageNegativeExample> ReadNegativeExamples() =>
        ReadNegativeExamplesAt(Path.Combine(FixtureRoot, "negative-examples.json"));

    public static IReadOnlyList<MediaPackageNegativeExample> ReadNegativeExamplesAt(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var results = new List<MediaPackageNegativeExample>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            results.Add(
                new MediaPackageNegativeExample(
                    element.GetProperty("name").GetString()!,
                    Convert.FromBase64String(element.GetProperty("manifest_base64").GetString()!),
                    element.GetProperty("manifest_sha256").GetString()!,
                    element.GetProperty("expected_code").GetString()!));
        }

        return results;
    }

    /// <summary>
    /// The per-file SHA-256 map held by the candidate's own <c>manifest.json</c>, keyed by file name.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadFixtureHashes()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(FixtureRoot, "manifest.json")));
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.GetProperty("sha256").EnumerateObject())
        {
            results.Add(entry.Name, entry.Value.GetString()!);
        }

        return results;
    }

    public static string ReadStagingRef(byte[] manifestBytes)
    {
        using var document = JsonDocument.Parse(manifestBytes);
        return document.RootElement.GetProperty("staging_ref").GetString()!;
    }

    public static LibraryId ReadLibraryId(byte[] manifestBytes)
    {
        using var document = JsonDocument.Parse(manifestBytes);
        return new LibraryId(
            Guid.Parse(document.RootElement.GetProperty("library_id").GetString()!));
    }

    private static List<MediaPackageExample> ReadExamplesAt(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var results = new List<MediaPackageExample>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var payload in element.GetProperty("file_payloads_base64").EnumerateObject())
            {
                payloads.Add(payload.Name, Convert.FromBase64String(payload.Value.GetString()!));
            }

            results.Add(
                new MediaPackageExample(
                    element.GetProperty("name").GetString()!,
                    Convert.FromBase64String(element.GetProperty("manifest_base64").GetString()!),
                    element.GetProperty("manifest_sha256").GetString()!,
                    payloads,
                    element.GetProperty("expected_relative_directory").GetString()!,
                    element.GetProperty("media_validity").GetString()!));
        }

        return results;
    }

    /// <summary>
    /// Walks up from both the test binary's directory and the current directory to the one directory
    /// that holds <c>AGENTS.md</c> and this project's committed fixtures at the complete
    /// <see cref="FixtureRelativeDirectory"/> path, so the platform runner works whether it starts in
    /// the output directory or in the repository. The fixtures are never copied to a second location:
    /// the tests read exactly the bytes under version control beside them.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var fixtureTail = FixtureRelativeDirectory.Replace('/', Path.DirectorySeparatorChar);
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var cursor = new DirectoryInfo(Path.GetFullPath(start));
            while (cursor is not null)
            {
                if (File.Exists(Path.Combine(cursor.FullName, "AGENTS.md"))
                    && Directory.Exists(Path.Combine(cursor.FullName, fixtureTail)))
                {
                    return cursor.FullName;
                }

                cursor = cursor.Parent;
            }
        }

        throw new InvalidOperationException(
            "The repository root holding AGENTS.md and " + FixtureRelativeDirectory + " was not found.");
    }
}

internal sealed record MediaPackageExample(
    string Name,
    byte[] ManifestBytes,
    string ManifestSha256,
    IReadOnlyDictionary<string, byte[]> Payloads,
    string ExpectedRelativeDirectory,
    string MediaValidity)
{
    public Sha256Digest Digest => new(ManifestSha256);
}

internal sealed record MediaPackageNegativeExample(
    string Name,
    byte[] ManifestBytes,
    string ManifestSha256,
    string ExpectedCode)
{
    public Sha256Digest Digest => new(ManifestSha256);
}

/// <summary>
/// One composed preflight under test, exposing the injected ports so a test can revoke a scope, deny
/// space or advance the clock between calls.
/// </summary>
internal sealed class MediaPackageComposition(
    MediaPackageSandbox sandbox,
    MediaPackageInspectionService service,
    MediaPackageScopeStub scope,
    MediaPackageSpaceStub space,
    TimeProvider clock,
    MediaPackageInspectionLimits limits) : IDisposable
{
    public MediaPackageSandbox Sandbox { get; } = sandbox;

    public MediaPackageScopeStub Scope { get; } = scope;

    public MediaPackageSpaceStub Space { get; } = space;

    public TimeProvider Clock { get; } = clock;

    public MediaPackageInspectionLimits Limits { get; } = limits;

    public void Dispose()
    {
        service.Dispose();
        Space.Dispose();
    }

    public IMediaPackageManifestReader Reader { get; } =
        new MediaPackageManifestReader(limits);

    public IMediaPackageInspectionPort Inspector { get; } =
        new IsolatedMediaPackageInspector(
            scope,
            new MediaPackageFileHasher(limits.StreamBufferByteCount),
            space);

    public ValueTask<MediaPackagePreflightReport> InspectAsync(
        byte[] manifestBytes,
        Sha256Digest expectedDigest,
        CancellationToken cancellationToken = default) =>
        service.InspectAsync(
            new MediaPackagePreflightRequest(
                new MediaPackageCallerContext("caller-test-1"),
                manifestBytes,
                expectedDigest),
            cancellationToken);

    public ValueTask<MediaPackagePreflightReport> InspectAsync(
        MediaPackageExample example,
        CancellationToken cancellationToken = default) =>
        InspectAsync(example.ManifestBytes, example.Digest, cancellationToken);

    /// <summary>
    /// Runs one preflight over the sandbox and proves the standing invariant of this feature: the whole
    /// sandbox tree (payload bytes and directory shape) is byte-identical afterwards, an absent
    /// <paramref name="errorCode"/> is rejected with no issue at all, and a present one is reported.
    /// </summary>
    public async ValueTask<MediaPackagePreflightReport> InspectAsync(
        MediaPackageExample example,
        string? errorCode,
        CancellationToken cancellationToken = default)
    {
        var before = Sandbox.SnapshotTree();
        var report = await InspectAsync(example.ManifestBytes, example.Digest, cancellationToken)
            .ConfigureAwait(false);
        CollectionAssert.AreEqual(
            before.ToArray(),
            Sandbox.SnapshotTree().ToArray(),
            "A preflight must not create, modify or remove anything.");
        if (errorCode is null)
        {
            Assert.AreEqual(
                MediaPackageInspectionStatus.Inspected,
                report.Status,
                Describe(report));
        }
        else
        {
            Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
            CollectionAssert.Contains(Codes(report), errorCode);
        }

        return report;
    }

    public static string[] Codes(MediaPackagePreflightReport report) =>
        [.. report.Issues.Select(issue => issue.Code).Distinct(StringComparer.Ordinal)];

    public static string Describe(MediaPackagePreflightReport report) =>
        $"status={report.Status} issues=[{string.Join(", ", report.Issues.Select(issue => issue.Code + "@" + (issue.Location ?? "-")))}]";
}
