namespace AssetLibrary.Windows.Client;

public sealed class WorkspaceState : IDisposable
{
    private CancellationTokenSource? pending;
    private CancellationTokenSource? detailPending;
    private readonly List<WorkspaceLocation> history = [];
    private readonly List<string?> pageCursors = [null];
    private int historyIndex = -1;
    private int generation;
    private int detailGeneration;
    private readonly ReadOnlyClient client;
    public WorkspaceState(ReadOnlyClient client) { this.client = client; }
    public event EventHandler? Changed;
    public WorkspaceLocation Location { get; private set; } = new();
    public IReadOnlyList<EntryItem> Entries { get; private set; } = [];
    public EntryItem? Detail { get; private set; }
    public string? NextCursor { get; private set; }
    public int PageNumber => pageCursors.Count;
    public bool CanBack => historyIndex > 0;
    public bool CanForward => historyIndex >= 0 && historyIndex < history.Count - 1;
    public bool IsBusy { get; private set; }
    public bool IsStale { get; private set; }
    public bool AccessRevoked { get; private set; }
    public string? Notice { get; private set; }

    public async Task NavigateAsync(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (historyIndex < history.Count - 1) { history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1); }
        history.Add(location);
        if (history.Count > 100) { history.RemoveAt(0); }
        historyIndex = history.Count - 1;
        await SetLocationAsync(location);
    }

    public async Task MoveHistoryAsync(int offset)
    {
        var target = historyIndex + offset;
        if (target < 0 || target >= history.Count) { return; }
        historyIndex = target;
        await SetLocationAsync(history[target]);
    }

    private async Task SetLocationAsync(WorkspaceLocation location)
    {
        Cancel();
        Location = location;
        Entries = [];
        Detail = null;
        Notice = null;
        IsStale = false;
        pageCursors.Clear();
        pageCursors.Add(null);
        NextCursor = null;
        if (location.IsHome) { Notify(); return; }
        await LoadPageAsync(null, false);
    }

    public Task RefreshAsync() => Location.IsHome ? Task.CompletedTask : LoadPageAsync(pageCursors[^1], true);

    public Task NextPageAsync()
    {
        if (NextCursor is null || IsBusy) { return Task.CompletedTask; }
        return ChangePageAsync(NextCursor, true);
    }

    public Task PreviousPageAsync()
    {
        if (pageCursors.Count < 2 || IsBusy) { return Task.CompletedTask; }
        return ChangePageAsync(pageCursors[^2], false);
    }

    private async Task ChangePageAsync(string? cursor, bool forward)
    {
        var previous = Entries;
        var expectedGeneration = generation + 1;
        var originalLocation = Location;
        await LoadPageAsync(cursor, true);
        if (generation != expectedGeneration || !ReferenceEquals(originalLocation, Location)
            || ReferenceEquals(previous, Entries) || IsStale || AccessRevoked) { return; }
        if (forward) { pageCursors.Add(cursor); }
        else { pageCursors.RemoveAt(pageCursors.Count - 1); }
        if (pageCursors.Count > 200) { pageCursors.RemoveAt(0); }
        Notify();
    }

    private async Task LoadPageAsync(string? cursor, bool preserve)
    {
        pending?.Cancel();

        using var request = new CancellationTokenSource();
        pending = request;
        var current = ++generation;
        IsBusy = true;
        Notice = null;
        ClearDetail();
        Notify();
        try
        {
            var page = await client.EntriesAsync(Location, cursor, request.Token);
            if (current != generation) { return; }
            Entries = page.Items;
            NextCursor = page.NextCursor;
            IsStale = false;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return; }
        catch (ClientException failure)
        {
            if (current != generation) { return; }
            if (failure.ClearsData) { Revoke(failure.Message); }
            else
            {
                if (!preserve) { Entries = []; }
                IsStale = Entries.Count > 0;
                Notice = IsStale ? $"显示上次读取的快照。{failure.Message}" : failure.Message;
            }
        }
        finally
        {
            if (current == generation) { IsBusy = false; pending = null; Notify(); }
        }
    }

    public async Task SelectAsync(EntryItem? item)
    {
        ClearDetail();
        Notify();
        if (item is null) { return; }
        using var request = new CancellationTokenSource();
        detailPending = request;
        var current = ++detailGeneration;
        try
        {
            var detail = await client.DetailAsync(item, request.Token);
            if (current == detailGeneration) { Detail = detail; Notify(); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return; }
        catch (ClientException failure)
        {
            if (current != detailGeneration) { return; }
            if (failure.ClearsData) { Revoke(failure.Message); }
            else { Notice = failure.Message; }
            Notify();
        }
        finally { if (current == detailGeneration) { detailPending = null; } }
    }

    public void ClearDetail()
    {
        ++detailGeneration;
        detailPending?.Cancel();
        detailPending = null;
        Detail = null;
    }

    public void Cancel()
    {
        ++generation;
        pending?.Cancel();
        pending = null;
        ClearDetail();
        IsBusy = false;
        Notify();
    }

    public void Revoke(string message)
    {
        Cancel();
        Entries = [];
        NextCursor = null;
        Location = new();
        history.Clear();
        historyIndex = -1;
        pageCursors.Clear();
        pageCursors.Add(null);
        AccessRevoked = true;
        IsStale = false;
        Notice = message;
        Notify();
    }

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() { Cancel(); Changed = null; }
}
