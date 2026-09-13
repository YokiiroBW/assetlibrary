using System.IO.Pipes;
using System.Runtime.Versioning;
using AssetLibrary.Windows.Session;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal sealed class ImagePipeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly List<NamedPipeServerStream> pipes = [];
    private readonly Task[] listeners;
    private readonly Func<ThumbnailRequest, CancellationToken, Task<ThumbnailResponse>> read;
    private readonly DerivedImageProfile profile;
    private readonly ImageClientCapacity capacity;

    internal ImagePipeServer(Func<ThumbnailRequest, CancellationToken, Task<ThumbnailResponse>> read, DerivedImageProfile profile, ImageClientCapacity capacity, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(read);
        this.read = read;
        this.profile = profile;
        this.capacity = capacity;
        try
        {
            for (var index = 0; index != 4; ++index) { pipes.Add(LocalPipe.CreateServer(endpoint, index == 0)); }
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
        if (!capacity.TryEnter()) { throw new IOException("Image client capacity reached."); }
        try { await ReadRequestAsync(pipe).ConfigureAwait(false); }
        finally { capacity.Exit(); }
    }

    private async Task ReadRequestAsync(NamedPipeServerStream pipe)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var firstFrame = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        firstFrame.CancelAfter(TimeSpan.FromMilliseconds(500));
        var bytes = new byte[48];
        await pipe.ReadExactlyAsync(bytes, firstFrame.Token).ConfigureAwait(false);
        var request = ImageProjectionProtocol.DecodeRequest(profile, bytes);
        // The sole extra read observes disconnect or protocol violation while HTTPS/native decode is pending.
        var disconnected = WatchDisconnectAsync(pipe, deadline);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            var response = await read(request, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            using var publication = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, response.Validity);
            publication.Token.ThrowIfCancellationRequested();
            await pipe.WriteAsync(ImageProjectionProtocol.EncodeResponse(profile, request.RequestId, response), publication.Token).ConfigureAwait(false);
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
