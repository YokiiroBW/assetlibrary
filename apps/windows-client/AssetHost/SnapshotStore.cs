using System.Text;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

public sealed class SnapshotStore : IAsyncDisposable
{
    private const int MaximumPages = 64;
    private const int MaximumTokens = 8192;
    private readonly object gate = new();
    private readonly ReadOnlyClient client;
    private readonly DateTimeOffset sessionExpires;
    private readonly TimeProvider clock;
    private readonly Action<string, SnapshotStatus, long>? log;
    private readonly Dictionary<Guid, PageLocation?> locations = [];
    private readonly Dictionary<Guid, PageState> pages = [];
    private readonly List<Task> work = [];
    private readonly ITimer expiryTimer;
    private CancellationTokenSource epochCancellation = new();
    private Guid epoch = Guid.NewGuid();
    private int running;
    private bool revoked;
    private bool stopped;

    public SnapshotStore(ReadOnlyClient client, DateTimeOffset sessionExpires, TimeProvider? clock = null,
        Action<string, SnapshotStatus, long>? log = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
        this.sessionExpires = sessionExpires;
        this.clock = clock ?? TimeProvider.System;
        this.log = log;
        expiryTimer = this.clock.CreateTimer(_ => CheckExpiry(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    // This method never awaits I/O. Only the two owned workers can reach Core.
    public SnapshotResponse Query(SnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (!revoked && clock.GetUtcNow() >= sessionExpires) { RevokeLocked(); }
            if (request.Epoch != Guid.Empty && request.Epoch != epoch) { return Status(SnapshotStatus.Expired); }
            if (revoked) { return Status(SnapshotStatus.AccessDenied); }
            if (stopped) { return Status(SnapshotStatus.Unavailable); }
            if ((request.Epoch == Guid.Empty) != (request.Node == Guid.Empty)) { return Status(SnapshotStatus.Expired); }
            if (!TryLocation(request.Node, out var location)) { return Status(SnapshotStatus.Expired); }
            if (!pages.TryGetValue(request.Node, out var page))
            {
                if (pages.Count == MaximumPages) { ExpireLocationsLocked(); return Status(SnapshotStatus.Expired); }
                page = new PageState();
                pages.Add(request.Node, page);
            }
            if (page.Pending) { return Status(SnapshotStatus.Loading); }
            var elapsed = clock.GetElapsedTime(page.Finished, clock.GetTimestamp());
            if (page.Response is not null && elapsed < Freshness(page.Response.Status)) { return page.Response; }
            page.Response = null;
            if (running == 2) { return Status(SnapshotStatus.Busy); }
            page.Pending = true;
            ++running;
            var expectedEpoch = epoch;
            var token = epochCancellation.Token;
            work.RemoveAll(task => task.IsCompleted);
            work.Add(Task.Run(() => LoadAsync(location!, page, expectedEpoch, token), CancellationToken.None));
            return Status(SnapshotStatus.Loading);
        }
    }

    private bool TryLocation(Guid node, out PageLocation? location)
    {
        if (node == Guid.Empty) { location = new PageLocation(new WorkspaceLocation(), null); return true; }
        return locations.TryGetValue(node, out location) && location is not null;
    }

    private async Task LoadAsync(PageLocation location, PageState page, Guid expectedEpoch, CancellationToken token)
    {
        var started = clock.GetTimestamp();
        var status = SnapshotStatus.Unavailable;
        try
        {
            var projection = await SnapshotProjection.ReadAsync(client, location, token).ConfigureAwait(false);
            lock (gate)
            {
                if (!CanPublish(expectedEpoch)) { return; }
                if (locations.Count + projection.Count > MaximumTokens) { ExpireLocationsLocked(); status = SnapshotStatus.Expired; return; }
                var items = new List<SnapshotItem>(projection.Count);
                foreach (var entry in projection)
                {
                    var node = Guid.NewGuid();
                    locations.Add(node, entry.Location);
                    items.Add(new SnapshotItem(node, entry.Kind, entry.Name));
                }
                status = SnapshotStatus.Ready;
                page.Response = new SnapshotResponse(status, epoch, items.AsReadOnly());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { status = SnapshotStatus.Expired; }
        catch (ClientException failure)
        {
            status = failure.ClearsData ? SnapshotStatus.AccessDenied
                : failure.Status == 410 ? SnapshotStatus.Expired
                : failure.Code == "invalid_response" ? SnapshotStatus.InvalidResponse : SnapshotStatus.Unavailable;
            lock (gate)
            {
                if (expectedEpoch != epoch) { return; }
                if (failure.ClearsData) { RevokeLocked(); }
                else if (failure.Status == 410) { ExpireLocationsLocked(); }
                else { page.Response = Status(status); }
            }
        }
        catch (Exception failure) when (failure is InvalidDataException or EncoderFallbackException)
        {
            status = SnapshotStatus.InvalidResponse;
            lock (gate) { if (expectedEpoch == epoch) { page.Response = Status(status); } }
        }
        finally
        {
            log?.Invoke("core_page", status, (long)clock.GetElapsedTime(started).TotalMilliseconds);
            lock (gate)
            {
                --running;
                page.Pending = false;
                page.Finished = clock.GetTimestamp();
            }
        }
    }

    private bool CanPublish(Guid expectedEpoch)
    {
        if (clock.GetUtcNow() >= sessionExpires && !revoked) { RevokeLocked(); }
        return expectedEpoch == epoch && !revoked && !stopped;
    }

    private static TimeSpan Freshness(SnapshotStatus status) => TimeSpan.FromSeconds(status == SnapshotStatus.Ready ? 5 : 1);
    private SnapshotResponse Status(SnapshotStatus status) => new(status, epoch, Array.Empty<SnapshotItem>());
    private void CheckExpiry()
    {
        lock (gate) { if (!stopped && !revoked && clock.GetUtcNow() >= sessionExpires) { RevokeLocked(); } }
    }
    private void RevokeLocked() { revoked = true; ExpireLocationsLocked(); }

    private void ExpireLocationsLocked()
    {
        epoch = Guid.NewGuid();
        locations.Clear();
        pages.Clear();
        epochCancellation.Cancel();
        epochCancellation.Dispose();
        epochCancellation = new CancellationTokenSource();
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (gate)
        {
            if (stopped) { return; }
            stopped = true;
            ExpireLocationsLocked();
            pending = [.. work];
        }
        await expiryTimer.DisposeAsync().ConfigureAwait(false);
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        finally { epochCancellation.Dispose(); }
    }

    internal (int Pages, int Tokens, int Running) Counts { get { lock (gate) { return (pages.Count, locations.Count, running); } } }
    private sealed class PageState
    {
        internal bool Pending { get; set; }
        internal long Finished { get; set; }
        internal SnapshotResponse? Response { get; set; }
    }
}
