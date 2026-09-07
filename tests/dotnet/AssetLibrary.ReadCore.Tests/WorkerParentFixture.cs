using System.Globalization;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;

namespace AssetLibrary.ReadCore.Tests;

// Built as a separate console only by test_worker_lifetime.py; it is not an MSTest case.
internal static class WorkerParentFixture
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length != 3)
        {
            return 2;
        }

        var state = arguments[2];
        var pidFile = Path.Combine(state, "child.pid");
        var root = Directory.CreateDirectory(Path.Combine(state, "assets")).FullName;
        await File.WriteAllTextAsync(Path.Combine(state, "parent.pid"), Environment.ProcessId.ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        var options = new ReadOnlyWorkerProcessOptions(arguments[0], [arguments[1], "block_after_request", pidFile],
            TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5));
        var target = new LibraryScanTarget(LibraryId.New(), StorageSourceId.New(),
            new CanonicalLibraryRoot(root, RootPathComparison.CaseInsensitive), StorageAvailability.Online);
        await foreach (var _ in new ProcessReadOnlyFileDiscovery(options).DiscoverAsync(target, CancellationToken.None).ConfigureAwait(false))
        {
        }

        return 0;
    }
}
