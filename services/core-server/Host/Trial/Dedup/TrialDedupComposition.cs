using AssetLibrary.Modules.AssetIdentity.Dedup.Application;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Infrastructure;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.AssetIdentity.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using AssetLibrary.Modules.TaskHealth.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AssetLibrary.CoreServer.Hosting.Trial;

/// <summary>
/// Everything the dedup workbench needs once, wired by the composition root. The task services are part
/// of it because the hosted worker is activated from the container: whoever starts a dedup job and
/// whoever runs it must be talking to the same durable task service for this workbench's task type.
/// </summary>
internal sealed record TrialDedupServices(
    DedupJobService Jobs,
    DedupJobWorker Worker,
    DedupReportRegistry Reports,
    IDedupSourceScopeQuery Scopes,
    ILibraryScanTargetQuery Libraries,
    DedupExecutionOptions Options,
    IDurableTaskCoordinator Coordinator,
    IDurableTaskInspector Inspector,
    IDurableTaskCommitGuard Guard);

/// <summary>
/// Composition root of the exact-duplicate workbench. It supplies the module's application service with
/// the ports it declares — the shared read-only discovery adapter, the bounded content reader, the
/// registered library roots read through the library module's public query, and TaskHealth's own
/// durable task service for this workbench's task type. No business rule lives here.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "A composition root necessarily names the implementations it wires. The count is the number of ports and adapters this one workbench needs, and no rule is implemented at this level.")]
internal static class TrialDedupComposition
{
    public static TrialDedupServices Create(
        TrialDatabaseConnections connections,
        TrialLibraryServices libraries,
        ILoggerFactory loggerFactory)
    {
        var options = DedupOptions();
        var scopes = new TrialDedupSourceScope(libraries.Store, libraries.Sources);
        var reports = new DedupReportRegistry(DedupJobContractText.MaximumRetainedReports);
        var tasks = CreateTasks(connections, loggerFactory);
        var execution = new PostgresTaskExecution(connections.Task);
        var jobs = new DedupJobService(CreateAnalyzer(scopes), reports, tasks, execution, options, TimeProvider.System);
        return new TrialDedupServices(
            jobs,
            new DedupJobWorker(jobs, reports, tasks, execution, options, TimeProvider.System),
            reports,
            scopes,
            libraries.Store,
            options,
            tasks,
            execution,
            execution);
    }

    /// <summary>
    /// Registers the task services this workbench's hosted worker is activated with. The container must
    /// hand out the very instances composed above: a second coordinator built from the same store would
    /// still be a different object graph, and a coordinator that was not filtered to this task type
    /// could claim another workbench's task and run it as an analysis.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(provider => provider.GetRequiredService<TrialDedupServices>().Coordinator);
        services.AddSingleton(provider => provider.GetRequiredService<TrialDedupServices>().Inspector);
        services.AddSingleton(provider => provider.GetRequiredService<TrialDedupServices>().Guard);
    }

    private static DedupExecutionOptions DedupOptions() => new()
    {
        LeaseDuration = TimeSpan.FromSeconds(30),
        HeartbeatInterval = TimeSpan.FromSeconds(2),
    };

    private static DedupAnalyzer CreateAnalyzer(IDedupSourceScopeQuery scopes) => new(
        new SystemDedupFileDiscovery(),
        new SystemDedupContentReader(),
        new SystemDedupSourceAvailability(),
        scopes,
        SystemDedupClock.Instance);

    /// <summary>
    /// One durable task service per task type: routing belongs to TaskHealth, while the task type name
    /// and the ceilings belong to this workbench's own contract. The same instance serves the three
    /// ports, because a coordinator, an inspector and a commit guard over one store must agree.
    /// </summary>
    private static DurableTaskService CreateTasks(TrialDatabaseConnections connections, ILoggerFactory loggerFactory) => new(
        new PostgresDurableTaskStore(connections.Task, DedupExecutionOptions.TaskType),
        TimeProvider.System,
        TaskHealthExecutionLimits.Default,
        new TaskHealthLogger(loggerFactory.CreateLogger<TaskHealthLogger>()));
}

/// <summary>
/// Runs claimed dedup tasks one at a time. It owns scheduling only: the work itself is the module's
/// worker, and the job's truth is the durable task. An unresolvable library fails the attempt instead
/// of being skipped, so a job never stays queued forever without a stated reason.
/// </summary>
internal sealed class TrialDedupWorker(
    TrialDedupServices dedup,
    IDurableTaskCoordinator tasks,
    ILogger<TrialDedupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                await tasks.ReclaimExpiredAsync(8, stoppingToken).ConfigureAwait(false);
                var leases = await tasks.ClaimAsync(
                    new DurableTaskClaimRequest(new LeaseOwner("trial-dedup"), dedup.Options.LeaseDuration, 1),
                    stoppingToken).ConfigureAwait(false);
                foreach (var lease in leases)
                {
                    worked = true;
                    await RunAsync(lease, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                TrialBackgroundLog.Unavailable(logger, "dedup");
            }

            if (worked)
            {
                continue;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async ValueTask RunAsync(DurableTaskLease lease, CancellationToken cancellationToken)
    {
        var payload = DedupJobPayloadReader.Read(lease.Payload);
        var target = await dedup.Libraries.FindAsync(payload.LibraryId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            // The library is gone, so the analysis cannot be attributed to a source. The attempt is
            // finished with a stated reason instead of being retried forever.
            _ = await tasks.FinishAsync(
                new DurableTaskFinishRequest(
                    lease.TaskId,
                    lease.Identity,
                    DurableTaskFinishKind.PermanentFailure,
                    new FailureCode("library_not_found"),
                    null),
                CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var source = new DedupResolvedSource(
            DedupSourceId.New(),
            target.LibraryId,
            target.Root,
            target.LibraryId.Value.ToString("D"));

        // The payload kind decides which work this attempt is. Both kinds come from the durable record,
        // so a reclaimed task re-runs what was requested rather than whatever the configuration says now.
        await dedup.Worker.RunAsync(lease, source, cancellationToken).ConfigureAwait(false);
    }
}
