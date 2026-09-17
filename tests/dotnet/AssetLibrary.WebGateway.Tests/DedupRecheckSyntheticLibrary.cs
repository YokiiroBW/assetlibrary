using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Application;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Infrastructure;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// A synthetic read-only library on disk, a real analysis of it, and the retained report that analysis
/// filed — the evidence a recheck is asked to re-verify. Every file is synthetic and lives under a
/// temporary directory this type owns and removes; nothing here can reach a real asset.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "Building one synthetic scenario inherently names the analysis entry point, its four ports, the report it files and the payload that names that report. Splitting it further would scatter one fixture's setup across files without removing a single dependency.")]
internal sealed class DedupRecheckSyntheticLibrary : IDisposable
{
    private const int RealContentLength = 2_048;
    private readonly string directory;

    private DedupRecheckSyntheticLibrary(
        string directory,
        DedupReportRegistry registry,
        DedupResolvedSource source,
        CanonicalLibraryRoot canonical,
        DedupJobPayloadReader.RecheckTarget target)
    {
        this.directory = directory;
        this.Registry = registry;
        this.Source = source;
        this.Canonical = canonical;
        this.Target = target;
    }

    public DedupReportRegistry Registry { get; }

    public DedupResolvedSource Source { get; }

    public CanonicalLibraryRoot Canonical { get; }

    /// <summary>The report version the recheck payload names, digest included.</summary>
    public DedupJobPayloadReader.RecheckTarget Target { get; }

    /// <summary>
    /// The version a reader of this library would see now: the newest retained report the library owns.
    /// It is what a late verdict must never replace.
    /// </summary>
    public (Guid TaskId, long Generation)? CurrentVersion =>
        Registry.TryLatest(Source.LibraryId.Value, out var key, out _) ? (key.TaskId, key.Generation) : null;

    /// <summary>The plan digest of one retained version, so a test can prove which one survived.</summary>
    public string? DigestOf(Guid taskId, long generation) =>
        Registry.TryGet(new DedupReportKey(taskId, generation), out var report) ? report.PlanDigest : null;

    /// <summary>
    /// The module's own analyzer over this library. A caller that needs to observe the walk supplies its
    /// own discovery; everything else is the product's real implementation, never a stand-in for it.
    /// </summary>
    public DedupAnalyzer AnalyzerFor(IDedupFileDiscovery discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        return new DedupAnalyzer(
            discovery,
            new SystemDedupContentReader(),
            new AlwaysOnlineAvailability(),
            new FixedScopeQuery(Canonical),
            new SystemDedupClock());
    }

    public static async Task<DedupRecheckSyntheticLibrary> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "assetlibrary-dedup-fence", Guid.NewGuid().ToString("N"));
        var root = Directory.CreateDirectory(Path.Combine(directory, "library")).FullName;
        WriteSyntheticPair(root);

        var canonical = new CanonicalLibraryRoot(root, RootPathComparison.CaseInsensitive);
        // A registered library, because that is the only role whose scope rule accepts a root the
        // installation itself lists: inbound staging must stay outside every registered root.
        var source = new DedupResolvedSource(
            new DedupSourceId(Guid.NewGuid()),
            new LibraryId(Guid.NewGuid()),
            canonical,
            "synthetic",
            DedupSourceRole.RegisteredLibrary);
        var registry = new DedupReportRegistry(8);
        var analyzer = new DedupAnalyzer(
            new SystemDedupFileDiscovery(),
            new SystemDedupContentReader(),
            new AlwaysOnlineAvailability(),
            new FixedScopeQuery(canonical),
            new SystemDedupClock());
        var limits = new DedupAnalysisLimits(
            MaximumFiles: 100,
            MaximumBytes: 8_000_000,
            MaximumFileBytes: 1_000_000);
        var target = await PublishAsync(analyzer, registry, source, canonical, limits);
        return new DedupRecheckSyntheticLibrary(directory, registry, source, canonical, target);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A handle that outlives the test is the operating system's to release.
        }
        catch (UnauthorizedAccessException)
        {
            // A read-only attribute left by a fixture is not worth failing a finished test over.
        }
    }

    /// <summary>
    /// Drops the retained version the recheck payload names, the way a lost lease or a superseding
    /// analysis leaves the registry behind. The key stops resolving, so only the fenced re-read at the end
    /// of a run can notice that the question it answered no longer has an owner.
    /// </summary>
    public void DropVersion() =>
        Registry.DiscardTask(Target.ReportTaskId);

    /// <summary>
    /// Runs a second real analysis of the same library and files it under a new job, the way a reader who
    /// starts the analysis again leaves the registry behind. The old version stays readable — retention
    /// keeps it — so a recheck bound to it cannot tell it has been replaced by asking whether its own key
    /// still resolves. That is exactly the case the previous round did not cover.
    /// </summary>
    public async Task<DedupJobPayloadReader.RecheckTarget> PublishNewerAsync()
    {
        var analyzer = AnalyzerFor(new SystemDedupFileDiscovery());
        return await PublishAsync(
            analyzer,
            Registry,
            Source,
            Canonical,
            new DedupAnalysisLimits(MaximumFiles: 100, MaximumBytes: 8_000_000, MaximumFileBytes: 1_000_000))
            .ConfigureAwait(false);
    }

    /// <summary>Two synthetic files with identical content, so a real pass finds exactly one group.</summary>
    private static void WriteSyntheticPair(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "set-a"));
        Directory.CreateDirectory(Path.Combine(root, "set-b"));
        var payload = new byte[RealContentLength];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(((index * 31) + 7) % 251);
        }

        File.WriteAllBytes(Path.Combine(root, "set-a", "original.bin"), payload);
        File.WriteAllBytes(Path.Combine(root, "set-b", "copy.bin"), payload);
    }

    /// <summary>
    /// Runs a real analysis and files its report, so a recheck compares against evidence a real pass
    /// produced rather than against a hand-built report the product could never file.
    /// </summary>
    private static async Task<DedupJobPayloadReader.RecheckTarget> PublishAsync(
        DedupAnalyzer analyzer,
        DedupReportRegistry registry,
        DedupResolvedSource source,
        CanonicalLibraryRoot canonical,
        DedupAnalysisLimits limits)
    {
        var taskId = Guid.NewGuid();
        var plan = await analyzer.AnalyzeAsync(
            new DedupAnalysisRequest(
                DedupAnalysisId.New(),
                [new DedupSourceRequest(source.SourceId, source.DisplayName, source.Role, source.LibraryId, canonical)],
                limits,
                TimeSpan.FromSeconds(30)),
            CancellationToken.None);
        var key = new DedupReportPublisher(registry).Publish(taskId, limits, source, plan);
        registry.RecordLimits(taskId, limits);
        return new DedupJobPayloadReader.RecheckTarget(taskId, key.Generation, plan.PlanDigest);
    }
}
