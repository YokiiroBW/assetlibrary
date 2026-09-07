using System.Text.Json;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Application;

namespace AssetLibrary.Modules.ScanReconciliation.Infrastructure;

public static class ReadOnlyScanWorker
{
    public static async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            var request = await ReadOnlyWorkerProtocol.ReadRequestAsync(input, cancellationToken).ConfigureAwait(false);
            var target = new LibraryScanTarget(LibraryId.New(), StorageSourceId.New(),
                new CanonicalLibraryRoot(request.CanonicalRoot,
                    request.CaseSensitive ? RootPathComparison.CaseSensitive : RootPathComparison.CaseInsensitive),
                StorageAvailability.Online);
            var count = 0;
            await foreach (var entry in new SystemReadOnlyFileDiscovery().DiscoverAsync(target, cancellationToken).ConfigureAwait(false))
            {
                count = checked(count + 1);
                await ReadOnlyWorkerProtocol.WriteAsync(output, new WorkerFrame(1, "entry",
                    RelativePath: entry.RelativePath.Value, Kind: (int)entry.Kind, ContentLength: entry.ContentLength,
                    LastWriteTimeUtc: entry.LastWriteTimeUtc, Attributes: (int)entry.Attributes), cancellationToken).ConfigureAwait(false);
            }

            await ReadOnlyWorkerProtocol.WriteAsync(output, new WorkerFrame(1, "complete", ObservedEntries: count),
                cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (FileDiscoveryException exception)
        {
            await ReadOnlyWorkerProtocol.WriteAsync(output, new WorkerFrame(1, "failure", Code: exception.FailureCode),
                CancellationToken.None).ConfigureAwait(false);
            return 20;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or OperationCanceledException or OverflowException)
        {
            await ReadOnlyWorkerProtocol.WriteAsync(output, new WorkerFrame(1, "failure", Code: "worker_discovery_failed"),
                CancellationToken.None).ConfigureAwait(false);
            return 20;
        }
    }
}
