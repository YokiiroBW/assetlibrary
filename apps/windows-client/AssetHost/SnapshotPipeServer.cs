using System.IO.Pipes;
using System.Runtime.Versioning;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
public sealed class SnapshotPipeServer : IAsyncDisposable
{
    private readonly SnapshotStore store;
    private readonly Action<string, SnapshotStatus, long>? log;
    private readonly CancellationTokenSource stopping = new();
    private readonly List<NamedPipeServerStream> pipes = [];
    private readonly Task[] listeners;
    private bool disposed;

    public SnapshotPipeServer(SnapshotStore store, Action<string, SnapshotStatus, long>? log = null) : this(store, NativePipe.EndpointName, log) { }

    internal SnapshotPipeServer(SnapshotStore store, string pipeName, Action<string, SnapshotStatus, long>? log = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.log = log;
        PipeName = pipeName;
        try
        {
            // Reserve all four instances before accepting. Retain them for the complete Host lifetime.
            for (var index = 0; index < 4; ++index) { pipes.Add(NativePipe.Create(PipeName, index == 0)); }
            listeners = pipes.Select(ListenAsync).ToArray();
        }
        catch
        {
            foreach (var pipe in pipes) { pipe.Dispose(); }
            stopping.Dispose();
            throw;
        }
    }

    public string PipeName { get; }
    public Task Completion => Task.WhenAny(listeners).Unwrap();

    private async Task ListenAsync(NamedPipeServerStream pipe)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                await ExchangeAsync(pipe).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (Exception failure) when (failure is IOException or InvalidDataException or OperationCanceledException)
            { log?.Invoke("pipe_exchange", SnapshotStatus.InvalidResponse, 0); }
            finally
            {
                if (pipe.IsConnected) { pipe.Disconnect(); }
            }
        }
    }

    private async Task ExchangeAsync(NamedPipeServerStream pipe)
    {
        if (!NativePipe.IsCurrentSession(pipe.SafePipeHandle)) { throw new IOException("Pipe client session rejected."); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(500));
        var bytes = new byte[48];
        await pipe.ReadExactlyAsync(bytes.AsMemory(0, SnapshotProtocol.HeaderSize), deadline.Token).ConfigureAwait(false);
        SnapshotProtocol.ValidateRequestHeader(bytes.AsSpan(0, SnapshotProtocol.HeaderSize));
        await pipe.ReadExactlyAsync(bytes.AsMemory(SnapshotProtocol.HeaderSize), deadline.Token).ConfigureAwait(false);
        var request = SnapshotProtocol.DecodeRequest(bytes);
        var response = SnapshotProtocol.EncodeResponse(request.RequestId, store.Query(request));
        await pipe.WriteAsync(response, deadline.Token).ConfigureAwait(false);
        // DisconnectNamedPipe discards unread output. Retain this bounded instance until the reader closes;
        // this is drain ownership, not an EOF requirement for decoding the already complete frame.
        var extra = new byte[1];
        if (await pipe.ReadAsync(extra, deadline.Token).ConfigureAwait(false) != 0)
        { throw new InvalidDataException("One exchange per connection required."); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) { return; }
        disposed = true;
        await stopping.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(listeners).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        finally
        {
            foreach (var pipe in pipes) { pipe.Dispose(); }
            stopping.Dispose();
        }
    }
}
