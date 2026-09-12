using System.IO.Pipes;
using System.Runtime.Versioning;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
public sealed class ThumbnailPipeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly List<NamedPipeServerStream> pipes = [];
    private readonly Task[] listeners;
    private readonly Func<ThumbnailRequest, CancellationToken, Task<ThumbnailResponse>> read;
    public static string Endpoint => "AssetLibrary.ExplorerThumbnail.v1." + LocalPipe.UserSession;

    public ThumbnailPipeServer(Func<ThumbnailRequest, CancellationToken, Task<ThumbnailResponse>> read, string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        this.read = read;
        try
        {
            for (var index = 0; index != 4; ++index) { pipes.Add(LocalPipe.CreateServer(endpoint ?? Endpoint, index == 0)); }
            listeners = pipes.Select(ListenAsync).ToArray();
        }
        catch { foreach (var pipe in pipes) { pipe.Dispose(); } stopping.Dispose(); throw; }
    }
    public Task Completion => Task.WhenAny(listeners).Unwrap();

    private async Task ListenAsync(NamedPipeServerStream pipe)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                try { await ExchangeAsync(pipe).ConfigureAwait(false); }
                catch (Exception failure) when (failure is IOException or InvalidDataException or OperationCanceledException)
                { /* Malformed or abandoned requests are confined to this one connection. */ }
                finally { pipe.Disconnect(); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { /* Normal listener stop. */ }
    }

    private async Task ExchangeAsync(NamedPipeServerStream pipe)
    {
        if (!LocalPipe.IsCurrentClientSession(pipe.SafePipeHandle)) { throw new IOException("Thumbnail client session rejected."); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var firstFrame = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        firstFrame.CancelAfter(TimeSpan.FromMilliseconds(500));
        var bytes = new byte[48];
        await pipe.ReadExactlyAsync(bytes, firstFrame.Token).ConfigureAwait(false);
        var request = ThumbnailProtocol.DecodeRequest(bytes);
        // The sole extra read observes disconnect or protocol violation while HTTPS/native decode is pending.
        var disconnected = WatchDisconnectAsync(pipe, deadline);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            var response = await read(request, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            using var publication = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, response.Validity);
            publication.Token.ThrowIfCancellationRequested();
            await pipe.WriteAsync(ThumbnailProtocol.EncodeResponse(request.RequestId, response), publication.Token).ConfigureAwait(false);
            try { await disconnected.WaitAsync(TimeSpan.FromMilliseconds(500), stopping.Token).ConfigureAwait(false); }
            catch (TimeoutException) { /* Bounded drain ends; the connection is then retired. */ }
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            await disconnected.ConfigureAwait(false);
        }
    }

    private static async Task WatchDisconnectAsync(NamedPipeServerStream pipe, CancellationTokenSource operation)
    {
        try { _ = await pipe.ReadAsync(new byte[1], operation.Token).ConfigureAwait(false); }
        catch (Exception failure) when (failure is IOException or OperationCanceledException)
        { /* Either EOF, an extra request byte, or interrupted I/O ends this request. */ }
        await operation.CancelAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => PipeListenerShutdown.CompleteAsync(stopping, listeners, pipes);
}
