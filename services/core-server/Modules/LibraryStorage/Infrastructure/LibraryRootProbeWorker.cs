using System.Text.Json;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.LibraryStorage.Infrastructure;

public static class LibraryRootProbeWorker
{
    public static async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            var request = await ReadOnlyWorkerProtocol.ReadRequestAsync(input, cancellationToken).ConfigureAwait(false);
            var comparison = request.CaseSensitive ? RootPathComparison.CaseSensitive : RootPathComparison.CaseInsensitive;
            var result = await new SystemLibraryRootProbe().ProbeAsync(request.CanonicalRoot, comparison, cancellationToken)
                .ConfigureAwait(false);
            await ReadOnlyWorkerProtocol.WriteAsync(output,
                new WorkerFrame(1, "probe", result.Status.ToString(), result.Root.Value), cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or OperationCanceledException)
        {
            await ReadOnlyWorkerProtocol.WriteAsync(output, new WorkerFrame(1, "failure", Code: "root_probe_failed"),
                CancellationToken.None).ConfigureAwait(false);
            return 20;
        }
    }
}
