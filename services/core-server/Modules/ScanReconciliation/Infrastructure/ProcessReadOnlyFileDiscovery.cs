using System.Runtime.CompilerServices;
using System.Text.Json;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Domain;

namespace AssetLibrary.Modules.ScanReconciliation.Infrastructure;

public sealed class ProcessReadOnlyFileDiscovery(ReadOnlyWorkerProcessOptions options) : IReadOnlyFileDiscovery
{
    public async IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(
        LibraryScanTarget target,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var count = 0;
        var complete = false;
        await using var frames = new ReadOnlyWorkerProcess(options).ReadAsync("scan",
            new WorkerRequest(1, target.Root.Value, target.Root.Comparison == RootPathComparison.CaseSensitive),
            cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (await MoveNextAsync(frames).ConfigureAwait(false))
        {
            var frame = frames.Current;
            if (complete)
            {
                throw new FileDiscoveryException("worker_multiple_terminal_frames");
            }

            if (frame.Type == "failure")
            {
                throw new FileDiscoveryException(SafeFailureCode(frame.Code));
            }

            if (frame.Type == "complete" && frame.ObservedEntries == count)
            {
                complete = true;
                continue;
            }

            if (frame.Type != "entry" || frame.RelativePath is null || frame.RelativePath.Length > 4096
                || frame.Kind is null || !Enum.IsDefined((AssetEntryKind)frame.Kind.Value)
                || frame.LastWriteTimeUtc is null || frame.Attributes is null)
            {
                throw new FileDiscoveryException("worker_entry_invalid");
            }

            var entry = ToEntry(frame);
            count = checked(count + 1);
            yield return entry;
        }

        if (!complete)
        {
            throw new FileDiscoveryException("worker_incomplete");
        }
    }

    private static DiscoveredEntry ToEntry(WorkerFrame frame)
    {
        try
        {
            var path = new RelativeAssetPath(frame.RelativePath!);
            var kind = (AssetEntryKind)frame.Kind!.Value;
            new AssetObservation(StableEntryId.New(), path, kind, frame.ContentLength, frame.LastWriteTimeUtc!.Value).Validate();
            return new DiscoveredEntry(path, kind, frame.ContentLength, frame.LastWriteTimeUtc.Value,
                (DiscoveryAttributes)frame.Attributes!.Value);
        }
        catch (ArgumentException)
        {
            throw new FileDiscoveryException("worker_entry_invalid");
        }
    }

    private static async ValueTask<bool> MoveNextAsync(IAsyncEnumerator<WorkerFrame> frames)
    {
        try
        {
            return await frames.MoveNextAsync().ConfigureAwait(false);
        }
        catch (ReadOnlyWorkerException exception)
        {
            throw new FileDiscoveryException(exception.Code);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new FileDiscoveryException("worker_protocol_invalid");
        }
    }

    private static string SafeFailureCode(string? code) =>
        code is { Length: > 0 and <= 100 } && code.All(character => char.IsAsciiLetterLower(character) || character == '_')
            ? code : "worker_discovery_failed";
}
