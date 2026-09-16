
namespace AssetLibrary.ReadCore.Tests;

/// <summary>
/// Isolated on-disk fixture for the read-only dedup slice. It is deliberately rooted in
/// .runtime/sandbox-storage so no test can reach a real library, and every assertion is made
/// against files this fixture created and verified itself.
/// </summary>
internal sealed class DedupScenario : IDisposable
{
    private readonly RepositorySandbox sandbox = new();
    private readonly List<string> junctions = [];
    private readonly List<CanonicalLibraryRoot> registeredRoots = [];
    private readonly List<CanonicalLibraryRoot> outputRoots = [];

    public string Root => sandbox.Root;

    public IReadOnlyList<CanonicalLibraryRoot> RegisteredRoots => registeredRoots;

    public IReadOnlyList<CanonicalLibraryRoot> OutputRoots => outputRoots;

    public CanonicalLibraryRoot RegisterRoot(string relativeRoot)
    {
        var root = CanonicalRoot(Path.Combine(Root, relativeRoot));
        registeredRoots.Add(root);
        return root;
    }

    public CanonicalLibraryRoot AddOutputRoot(string relativeRoot)
    {
        var root = CanonicalRoot(Path.Combine(Root, relativeRoot));
        outputRoots.Add(root);
        return root;
    }

    /// <summary>Root for a directory that this installation does not have registered.</summary>
    public CanonicalLibraryRoot UnregisteredRoot(string relativeRoot) =>
        CanonicalRoot(Path.Combine(Root, relativeRoot));

    public static CanonicalLibraryRoot CanonicalRoot(string absolute) =>
        new(
            absolute,
            OperatingSystem.IsWindows() ? RootPathComparison.CaseInsensitive : RootPathComparison.CaseSensitive);

    public DirectoryInfo CreateDirectory(string relativePath) =>
        Directory.CreateDirectory(EnsureInside(relativePath));

    public string AbsolutePath(string relativePath) => EnsureInside(relativePath);

    /// <summary>
    /// Creates a real directory junction below the sandbox unless one of the same name already
    /// exists. It is used only to prove that a reparse point is refused, never to reach outside
    /// content.
    /// </summary>
    public static void CreateDirectoryJunction(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var helper = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "/d", "/c", "mklink", "/J", link, target },
            }) ?? throw new InvalidOperationException("The junction helper did not start.");
            helper.WaitForExit();
            if (helper.ExitCode != 0)
            {
                throw new InvalidOperationException("The junction helper failed.");
            }

            return;
        }

        Directory.CreateSymbolicLink(link, target);
    }

    public string TrackJunction(string link)
    {
        junctions.Add(link);
        return link;
    }

    /// <summary>Writes bytes and returns the physical path, so a test can compare against it later.</summary>
    public string WriteFile(string relativePath, byte[] content)
    {
        var path = EnsureInside(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public string WriteText(string relativePath, string content) =>
        WriteFile(relativePath, System.Text.Encoding.UTF8.GetBytes(content));

    /// <summary>
    /// Writes a deterministic file of the requested size, used to cross the reader's internal
    /// stream buffer and to prove that two equal-length files can still differ.
    /// </summary>
    public string WritePatterned(string relativePath, int length, byte seed = 7)
    {
        var content = new byte[length];
        for (var index = 0; index < content.Length; index++)
        {
            // The seed must change the first bytes too, otherwise two "different" fixtures would
            // still be byte-identical at the front of the file.
            content[index] = (byte)((index * 31 + seed * 3 + 1) % 251);
        }

        return WriteFile(relativePath, content);
    }

    /// <summary>Changes a file's content and moves its write time forward.</summary>
    public void Mutate(string relativePath, byte[] content)
    {
        var path = WriteFile(relativePath, content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
    }

    public void Delete(string relativePath) => File.Delete(EnsureInside(relativePath));

    /// <summary>Runs one analysis against this scenario with the real read-only adapters.</summary>
    public Task<DedupCurationPlan> AnalyzeAsync(
        DedupAnalysisRequest request,
        CancellationToken cancellationToken = default) =>
        DedupAnalysisFactory.Create(this).AnalyzeAsync(request, cancellationToken);

    public void Dispose()
    {
        foreach (var junction in junctions)
        {
            if (Directory.Exists(junction)
                && (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0)
            {
                // A junction must be removed by link, never recursively through its target.
                Directory.Delete(junction);
            }
        }

        sandbox.Dispose();
    }

    private string EnsureInside(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(Root, relativePath));
        var prefix = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException("A dedup fixture path must stay inside its sandbox.");
        }

        return path;
    }
}

/// <summary>Builds analyzer instances for tests. Adapters are always the real read-only ones.</summary>
internal static class DedupAnalysisFactory
{
    public static DedupAnalyzer Create(DedupScenario scenario, IDedupClock? clock = null) =>
        Create(scenario, new SystemDedupContentReader(), discovery: null, clock);

    public static DedupAnalyzer Create(DedupScenario scenario, IDedupContentReader reader) =>
        Create(scenario, reader, discovery: null, clock: null);

    public static DedupAnalyzer Create(
        DedupScenario scenario,
        IDedupContentReader reader,
        IDedupFileDiscovery? discovery) =>
        Create(scenario, reader, discovery, clock: null);

    public static DedupAnalyzer Create(
        DedupScenario scenario,
        IDedupContentReader reader,
        IDedupFileDiscovery? discovery,
        IDedupClock? clock) =>
        new(
            discovery ?? new SystemDedupFileDiscovery(),
            reader,
            new SystemDedupSourceAvailability(),
            new TestSourceScope(scenario.RegisteredRoots, scenario.OutputRoots),
            clock ?? new FixedDedupClock(DateTimeOffset.UnixEpoch));

    public static DedupAnalysisRequest Request(
        IReadOnlyList<DedupSourceRequest> sources,
        DedupAnalysisLimits? limits = null,
        TimeSpan? timeout = null) =>
        new(
            DedupAnalysisId.New(),
            sources,
            limits ?? DedupAnalysisLimits.Default,
            timeout ?? TimeSpan.FromSeconds(30));

    /// <summary>Runs one analysis with the real read-only adapters.</summary>
    public static Task<DedupCurationPlan> RunAsync(
        DedupScenario scenario,
        DedupAnalysisRequest request,
        CancellationToken cancellationToken = default) =>
        Create(scenario).AnalyzeAsync(request, cancellationToken);

    /// <summary>Runs a recheck of a stored preview with the real read-only adapters.</summary>
    public static Task<DedupRecountResult> RecountAsync(
        DedupScenario scenario,
        DedupRecountRequest request,
        CancellationToken cancellationToken = default) =>
        Create(scenario).RecountAsync(request, cancellationToken);

    public static DedupSourceRequest Source(
        CanonicalLibraryRoot root,
        string displayName,
        DedupSourceRole role = DedupSourceRole.InboundStaging,
        LibraryId? libraryId = null) =>
        new(
            DedupSourceId.New(),
            displayName,
            role,
            libraryId ?? LibraryId.New(),
            root);

    public static DedupPlanItem Item(DedupCurationPlan plan, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Items.Single(item => item.PathText == relativePath);
    }

    /// <summary>Sampled head and tail fingerprint of one analysed file, or zero when it was not read.</summary>
    public static long StructureHash(DedupCurationPlan plan, string relativePath) =>
        Item(plan, relativePath).StructureHash;

    /// <summary>A compact, order-stable listing of the paths a preview observed.</summary>
    public static string PathList(DedupCurationPlan plan) =>
        string.Join(',', plan.Items.Select(item => item.PathText));

    public static bool HasRelation(DedupCurationPlan plan, string relativePath, AssetRelation relation) =>
        Item(plan, relativePath).Relations.Contains(relation);
}

internal sealed class TestSourceScope(
    IReadOnlyList<CanonicalLibraryRoot> registered,
    IReadOnlyList<CanonicalLibraryRoot> outputs) : IDedupSourceScopeQuery
{
    public ValueTask<IReadOnlyList<CanonicalLibraryRoot>> RegisteredRootsAsync(
        CancellationToken cancellationToken) => ValueTask.FromResult(registered);

    public ValueTask<IReadOnlyList<CanonicalLibraryRoot>> ManagedOutputRootsAsync(
        CancellationToken cancellationToken) => ValueTask.FromResult(outputs);
}

internal sealed class FixedDedupClock(DateTimeOffset now) : IDedupClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>
/// Records every content read the analyzer asked for, then defers to the real streaming reader, so
/// a test can prove which files were actually opened.
/// </summary>
internal sealed class RecordingContentReader : IDedupContentReader
{
    private readonly SystemDedupContentReader streaming = new();

    public List<DedupContentReadRequest> Requests { get; } = [];

    public IReadOnlyList<ContentReadResult> Results { get; private set; } = [];

    public async Task<IReadOnlyList<ContentReadResult>> ReadAsync(
        IReadOnlyList<DedupContentReadRequest> requests,
        int concurrency,
        CancellationToken cancellationToken)
    {
        Requests.AddRange(requests);
        Results = await streaming.ReadAsync(requests, concurrency, cancellationToken);
        return Results;
    }
}

/// <summary>
/// Discovery that reports one extra relative name beside the files the walk found. Both entries
/// share a content length so the analysis actually tries to read them.
/// </summary>
internal sealed class EscapingDiscovery : IDedupFileDiscovery
{
    private readonly string extraName;

    public EscapingDiscovery(CanonicalLibraryRoot root, string extraName)
    {
        Root = root;
        this.extraName = extraName;
    }

    public CanonicalLibraryRoot Root { get; }

    public async IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
        CanonicalLibraryRoot candidate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new DiscoveredFile(
            new RelativeAssetPath("legit.bin"),
            IsReparsePoint: false,
            IsExcluded: false,
            Length: 28,
            DateTimeOffset.UnixEpoch);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new DiscoveredFile(
            new RelativeAssetPath(extraName),
            IsReparsePoint: false,
            IsExcluded: false,
            Length: 28,
            DateTimeOffset.UnixEpoch);
        _ = candidate;
    }
}

/// <summary>Discovery that fails on one designated root, so an aborted walk stays honest.</summary>
internal sealed class FailingSecondRootDiscovery : IDedupFileDiscovery
{
    private readonly CanonicalLibraryRoot failingRoot;
    private readonly string failureCode;

    public FailingSecondRootDiscovery(CanonicalLibraryRoot failingRoot, string failureCode)
    {
        this.failingRoot = failingRoot;
        this.failureCode = failureCode;
    }

    public async IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
        CanonicalLibraryRoot candidate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (candidate.Value == failingRoot.Value)
        {
            await Task.Yield();
            throw new DedupDiscoveryException(failureCode);
        }

        await foreach (var entry in new SystemDedupFileDiscovery()
            .DiscoverAsync(candidate, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return entry;
        }

        _ = cancellationToken;
    }
}

/// <summary>Discovery that never completes, so an exhausted deadline is observable.</summary>
internal sealed class BlockingDiscovery : IDedupFileDiscovery
{
    private readonly CanonicalLibraryRoot root;

    public BlockingDiscovery(CanonicalLibraryRoot root) => this.root = root;

    public async IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
        CanonicalLibraryRoot candidate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = root;
        _ = candidate;
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }
}

/// <summary>
/// Discovery that deletes a file after reporting it, so the read fails the way a file that moved
/// away mid-analysis would.
/// </summary>
internal sealed class VanishingFileDiscovery : IDedupFileDiscovery
{
    private readonly DedupScenario scenario;
    private readonly string victim;

    public VanishingFileDiscovery(DedupScenario scenario, string victim)
    {
        this.scenario = scenario;
        this.victim = victim;
    }

    public async IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
        CanonicalLibraryRoot candidate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in new SystemDedupFileDiscovery()
            .DiscoverAsync(candidate, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return entry;
        }

        if (File.Exists(scenario.AbsolutePath(victim)))
        {
            scenario.Delete(victim);
        }
    }
}
