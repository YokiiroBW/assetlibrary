using AssetLibrary.Modules.AssetIdentity.Dedup.Application;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Infrastructure;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// One claimed recheck over a synthetic library, observed through <see cref="FenceProbeGuard"/>. It owns
/// the synthetic scenario only to build the worker and then hand it to the probe, so a test states the
/// fence property without a database, a host or a real share.
/// </summary>
internal sealed class DedupRecheckFenceFixture : IDisposable
{
    private readonly DedupRecheckSyntheticLibrary library;
    private readonly FenceProbeGuard guard;
    private readonly DedupJobWorker worker;
    private readonly ProbeDiscovery discovery;

    private DedupRecheckFenceFixture(
        DedupRecheckSyntheticLibrary library,
        FenceProbeGuard guard,
        DedupJobWorker worker,
        ProbeDiscovery discovery)
    {
        this.library = library;
        this.guard = guard;
        this.worker = worker;
        this.discovery = discovery;
        discovery.Fixture = this;
    }

    /// <summary>Every file the recheck really read, which is what makes the fence claim meaningful.</summary>
    public int ScanReads => discovery.Observation.Reads;

    /// <summary>How many of those reads happened while the durable commit was open.</summary>
    public int ReadsWhileFenced => discovery.Observation.ReadsWhileFenced;

    public int Commits => guard.Commits;

    public int MaximumOpenCommits => guard.MaximumOpen;

    public int OpenCommits => guard.Open;

    public bool ScanWasCancelled => discovery.Observation.WasCancelled;

    /// <summary>The verdict the recheck filed, if it reached one.</summary>
    public DedupRecheckRun? Run { get; private set; }

    /// <summary>When set, a newer analysis lands while the scan is running.</summary>
    public bool ReplacedDuringScan { get; set; }

    /// <summary>When set, the scan is cancelled from inside the walk.</summary>
    public bool CancelDuringScan { get; set; }

    public static async Task<DedupRecheckFenceFixture> CreateAsync()
    {
        var library = await DedupRecheckSyntheticLibrary.CreateAsync();
        var discovery = new ProbeDiscovery(library.Canonical);
        // The same analyzer the scenario used, wrapped only where the walk is observed, so the recheck
        // under test runs the module's real code and not a stand-in for it.
        var analyzer = library.AnalyzerFor(discovery);
        var guard = new FenceProbeGuard();
        var coordinator = new InertTaskCoordinator();
        var options = new DedupExecutionOptions
        {
            RecheckTimeout = TimeSpan.FromMinutes(1),
            HeartbeatInterval = TimeSpan.FromMilliseconds(50),
            LeaseDuration = TimeSpan.FromSeconds(30),
        };
        var worker = new DedupJobWorker(
            new DedupJobService(
                analyzer,
                library.Registry,
                coordinator,
                new InertTaskInspector(),
                new AlwaysOnlineAvailability(),
                options,
                TimeProvider.System),
            library.Registry,
            coordinator,
            guard,
            options,
            TimeProvider.System);
        var fixture = new DedupRecheckFenceFixture(library, guard, worker, discovery);
        discovery.Fixture = fixture;
        return fixture;
    }

    /// <summary>
    /// Runs one claimed recheck exactly the way the host does: a lease naming one report version, the
    /// module's own worker, and the fence the verdict has to be filed under.
    /// </summary>
    public async Task RunRecheckAsync()
    {
        var target = library.Target;
        var lease = new DurableTaskLease(
            new DurableTaskId(Guid.NewGuid()),
            DedupExecutionOptions.TaskType,
            DedupJobPayload.CreateRecheck(
                library.Source.LibraryId,
                target.ReportTaskId,
                target.Generation,
                target.PlanDigest),
            TaskPriority.P3,
            Attempt: 1,
            MaxAttempts: 2,
            new TaskLeaseIdentity(new LeaseOwner("fixture"), new LeaseToken(Guid.NewGuid()), 1),
            DateTimeOffset.UtcNow.AddMinutes(5),
            CancellationRequested: false);
        await worker.RunAsync(lease, library.Source, CancellationToken.None);
        Run = library.Registry.RecheckOf(lease.TaskId.Value);
    }

    public void Dispose() => library.Dispose();

    /// <summary>
    /// Drops the retained version while the scan is running, which is what a lost lease or a superseding
    /// analysis leaves behind: the key stops resolving, and only the fenced re-read at the end notices.
    /// </summary>
    internal void Supersede() => library.DropVersion();

    /// <summary>Counts the files a walk visited and cancels one from inside it, when a test asks for that.</summary>
    internal sealed class ProbeObservation
    {
        public int Reads { get; private set; }

        public int ReadsWhileFenced { get; private set; }

        public bool WasCancelled { get; private set; }

        public void Visited(int openCommits)
        {
            Reads++;
            if (openCommits > 0)
            {
                ReadsWhileFenced++;
            }
        }

        public bool ShouldCancel(bool cancelDuringScan)
        {
            WasCancelled = cancelDuringScan;
            return cancelDuringScan;
        }
    }

    /// <summary>Reads the synthetic files while proving no fence is open, and reports when it was refused.</summary>
    internal sealed class ProbeDiscovery(CanonicalLibraryRoot root) : IDedupFileDiscovery
    {
        private readonly SystemDedupFileDiscovery inner = new();

        public DedupRecheckFenceFixture? Fixture { get; set; }

        public ProbeObservation Observation { get; } = new();

        public async IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
            CanonicalLibraryRoot candidate,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var file in inner.DiscoverAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                Observation.Visited(Fixture?.guard.Open ?? 0);
                if (Fixture is not null && Observation.ShouldCancel(Fixture.CancelDuringScan))
                {
                    throw new OperationCanceledException("The recheck was cancelled while it was reading.");
                }

                if (Fixture is { ReplacedDuringScan: true })
                {
                    // A newer analysis lands mid-scan, which is the race a long walk makes possible.
                    Fixture.ReplacedDuringScan = false;
                    Fixture.Supersede();
                }

                yield return file;
            }

            _ = root;
        }
    }
}
