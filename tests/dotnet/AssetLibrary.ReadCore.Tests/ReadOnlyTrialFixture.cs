using AssetLibrary.Modules.AssetIdentity.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;
using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using AssetLibrary.Modules.TaskHealth.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AssetLibrary.ReadCore.Tests;

internal sealed class ReadOnlyTrialFixture : IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;
    private readonly NpgsqlDataSource admin;
    public RepositorySandbox Sandbox { get; }
    public PostgresLibraryStore Libraries { get; }
    public LibraryRegistrationService Registration { get; }
    public LibraryAvailabilityService Availability { get; }
    public PostgresAssetObservationSink Sink { get; }
    public PostgresAssetIndexSnapshotQuery Snapshots { get; }
    public IScanRequestStore Store { get; }
    public DurableTaskService Tasks { get; }
    public PostgresTaskExecution Execution { get; }
    public InitialScanCoordinator Scans { get; }
    public Guid PrincipalId { get; } = Guid.NewGuid();

    public ReadOnlyTrialFixture(IReadOnlyFileDiscovery? discovery = null,
        Func<IScanRequestStore, IScanRequestStore>? decorateStore = null, InitialScanExecutionOptions? options = null)
    {
        var connection = RequiredEnvironment("ASSETLIBRARY_TRIAL_TEST_CONNECTION");
        Sandbox = new RepositorySandbox();
        dataSource = NpgsqlDataSource.Create(connection);
        admin = NpgsqlDataSource.Create(RequiredEnvironment("ASSETLIBRARY_TRIAL_TEST_ADMIN_CONNECTION"));
        var libraries = new TrialLibraryServices(dataSource, Sandbox);
        Libraries = libraries.Store;
        Registration = libraries.Registration;
        Availability = libraries.Availability;
        Sink = new PostgresAssetObservationSink(dataSource);
        Snapshots = new PostgresAssetIndexSnapshotQuery(dataSource);
        var taskRuntime = new TrialTaskServices(dataSource);
        Tasks = taskRuntime.Tasks;
        Execution = taskRuntime.Execution;
        var scans = new TrialScanServices(dataSource, libraries, Sink, Snapshots, taskRuntime, discovery, decorateStore, options);
        Store = scans.Store;
        Scans = scans.Scans;
    }

    public ManagementOperation Operation() => new(PrincipalId, Guid.NewGuid());

    public async ValueTask<LibraryId> RegisterAsync(string name = "library")
    {
        var directory = Directory.CreateDirectory(Path.Combine(Sandbox.Root, name));
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "summer-photo.jpg"), "read-only-content");
        return await Registration.RegisterAsync(new LibraryRegistrationRequest("sandbox", name, directory.FullName), Operation(), CancellationToken.None);
    }

    public async Task<object?> SqlAsync(string sql)
    {
        await using var command = admin.CreateCommand(sql);
        command.CommandTimeout = 10;
        return await command.ExecuteScalarAsync();
    }

    public static ReadOnlyWorkerProcessOptions WorkerOptions() => new(
        RequiredEnvironment("ASSETLIBRARY_TEST_DOTNET"), [RequiredEnvironment("ASSETLIBRARY_TEST_HOST_DLL")],
        probeTimeout: TimeSpan.FromSeconds(3));

    public static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            Assert.Inconclusive("Native read-only integration is provided by the required PostgreSQL/Host test runner.");
        }

        return value!;
    }

    public async ValueTask DisposeAsync()
    {
        await dataSource.DisposeAsync();
        await admin.DisposeAsync();
        Sandbox.Dispose();
    }
}

internal sealed class TrialLibraryServices
{
    public PostgresLibraryStore Store { get; }
    public LibraryRegistrationService Registration { get; }
    public LibraryAvailabilityService Availability { get; }
    public TrialLibraryServices(NpgsqlDataSource dataSource, RepositorySandbox sandbox)
    {
        Store = new PostgresLibraryStore(dataSource);
        var probe = new ProcessLibraryRootProbe(ReadOnlyTrialFixture.WorkerOptions());
        ConfiguredStorageSource[] sources = [new(StorageSourceId.New(), "sandbox", "Sandbox",
            new CanonicalLibraryRoot(sandbox.Root, OperatingSystem.IsWindows() ? RootPathComparison.CaseInsensitive : RootPathComparison.CaseSensitive))];
        Registration = new LibraryRegistrationService(Store, probe, sources, TimeProvider.System);
        Availability = new LibraryAvailabilityService(Store, probe, sources, TimeProvider.System);
    }
}
internal sealed class TrialTaskServices
{
    public DurableTaskService Tasks { get; }
    public PostgresTaskExecution Execution { get; }
    public TrialTaskServices(NpgsqlDataSource dataSource)
    {
        Tasks = new DurableTaskService(new PostgresDurableTaskStore(dataSource, InitialScanExecutionOptions.TaskType),
            TimeProvider.System, TaskHealthExecutionLimits.Default, new TaskHealthLogger(NullLogger<TaskHealthLogger>.Instance));
        Execution = new PostgresTaskExecution(dataSource);
    }
}
internal sealed class TrialScanServices
{
    public IScanRequestStore Store { get; }
    public InitialScanCoordinator Scans { get; }
    public TrialScanServices(NpgsqlDataSource dataSource, TrialLibraryServices libraries,
        PostgresAssetObservationSink sink, PostgresAssetIndexSnapshotQuery snapshots, TrialTaskServices tasks,
        IReadOnlyFileDiscovery? discovery, Func<IScanRequestStore, IScanRequestStore>? decorateStore, InitialScanExecutionOptions? options)
    {
        var store = new PostgresScanStore(dataSource);
        Store = decorateStore?.Invoke(store) ?? store;
        var recovery = new ScanTaskRecovery(Store, snapshots, snapshots, tasks.Tasks, tasks.Execution, TimeProvider.System);
        var limits = options ?? new InitialScanExecutionOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(50), BatchSize = 1 };
        var executor = new InitialScanTaskExecutor(Store, libraries.Store, libraries.Availability,
            discovery ?? new ProcessReadOnlyFileDiscovery(ReadOnlyTrialFixture.WorkerOptions()), sink, tasks.Tasks, tasks.Execution,
            recovery, TimeProvider.System, limits, new InitialScanLogger(NullLogger<InitialReadOnlyScanService>.Instance));
        Scans = new InitialScanCoordinator(Store, libraries.Store, libraries.Availability, snapshots, tasks.Tasks,
            tasks.Execution, recovery, executor, limits);
    }
}

internal static class ReadOnlyTrialTaskClaim
{
    public static async ValueTask<(DurableTaskLease Lease, ScanRequestRecord Request)> ClaimAsync(this ReadOnlyTrialFixture fixture)
    {
        var lease = (await fixture.Tasks.ClaimAsync(new DurableTaskClaimRequest(new LeaseOwner("fixture-worker"),
            TimeSpan.FromMinutes(1)), CancellationToken.None))[0];
        var request = await fixture.Store.FindAsync(lease.TaskId, CancellationToken.None);
        return (lease, request!);
    }

}
