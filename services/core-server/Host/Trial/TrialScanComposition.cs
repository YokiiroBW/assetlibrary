using AssetLibrary.Modules.AssetIdentity.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;
using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using AssetLibrary.Modules.TaskHealth.Infrastructure;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialScanComposition
{
    public static IInitialScanCoordinator Create(
        TrialDatabaseConnections connections,
        TrialLibraryServices libraries,
        ReadOnlyWorkerProcessOptions workers,
        ILoggerFactory loggerFactory)
    {
        var sink = new PostgresAssetObservationSink(connections.Asset);
        var snapshot = new PostgresAssetIndexSnapshotQuery(connections.Asset);
        var store = new PostgresScanStore(connections.Scan);
        var options = new InitialScanExecutionOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromSeconds(2),
        };
        var tasks = new DurableTaskService(new PostgresDurableTaskStore(connections.Task, InitialScanExecutionOptions.TaskType),
            TimeProvider.System, TaskHealthExecutionLimits.Default, new TaskHealthLogger(loggerFactory.CreateLogger<TaskHealthLogger>()));
        var execution = new PostgresTaskExecution(connections.Task);
        var recovery = new ScanTaskRecovery(store, snapshot, snapshot, tasks, execution, TimeProvider.System);
        var executor = new InitialScanTaskExecutor(store, libraries.Store, libraries.Availability,
            new ProcessReadOnlyFileDiscovery(workers), sink, tasks, execution, recovery, TimeProvider.System, options,
            new InitialScanLogger(loggerFactory.CreateLogger<InitialReadOnlyScanService>()));
        return new InitialScanCoordinator(store, libraries.Store, libraries.Availability, snapshot, tasks, execution, recovery, executor, options);
    }
}
