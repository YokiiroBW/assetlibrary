using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.LibraryStorage.Infrastructure;

public sealed class ProcessLibraryRootProbe(ReadOnlyWorkerProcessOptions options) : ILibraryRootProbe
{
    public async ValueTask<LibraryRootProbeResult> ProbeAsync(
        string path,
        RootPathComparison comparison,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.ProbeTimeout);
        LibraryRootProbeResult? result = null;
        try
        {
            await foreach (var frame in new ReadOnlyWorkerProcess(options).ReadAsync("probe",
                new WorkerRequest(1, path, comparison == RootPathComparison.CaseSensitive), deadline.Token).ConfigureAwait(false))
            {
                if (frame.Type != "probe" || result is not null
                    || !Enum.TryParse<LibraryRootProbeStatus>(frame.Status, out var status) || !Enum.IsDefined(status)
                    || frame.CanonicalRoot is null)
                {
                    throw new ReadOnlyWorkerException("worker_probe_invalid");
                }

                result = new LibraryRootProbeResult(status, new CanonicalLibraryRoot(frame.CanonicalRoot, comparison));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReadOnlyTrialException("root_probe_timed_out");
        }
        catch (IOException)
        {
            throw new ReadOnlyTrialException("root_probe_failed");
        }

        return result ?? throw new ReadOnlyTrialException("root_probe_failed");
    }
}
