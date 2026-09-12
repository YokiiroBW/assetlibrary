using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text.Json;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
public sealed class ControlPipeServer : IAsyncDisposable
{
    private readonly UserSessionCoordinator session;
    private readonly CancellationTokenSource stopping = new();
    private readonly List<NamedPipeServerStream> pipes = [];
    private readonly Task[] listeners;
    private readonly TaskCompletionSource shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ControlPipeServer(UserSessionCoordinator session, string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.session = session;
        try
        {
            for (var index = 0; index < 4; ++index) { pipes.Add(LocalPipe.CreateServer(endpoint ?? LocalPipe.ControlEndpoint, index == 0)); }
            listeners = pipes.Select(ListenAsync).ToArray();
        }
        catch { foreach (var pipe in pipes) { pipe.Dispose(); } stopping.Dispose(); throw; }
    }
    public Task Completion => Task.WhenAny(listeners.Append(shutdown.Task)).Unwrap();

    private async Task ListenAsync(NamedPipeServerStream pipe)
    {
        try
        {
            while (true)
            {
                await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                try { await ExchangeAsync(pipe).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException or OperationCanceledException or ArgumentException)
                { /* A rejected/abandoned frame cannot change another session or terminate the listener. */ }
                finally { pipe.Disconnect(); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { /* Bounded listener shutdown. */ }
    }

    private async Task ExchangeAsync(NamedPipeServerStream pipe)
    {
        if (!LocalPipe.IsCurrentClientSession(pipe.SafePipeHandle)) { throw new IOException("Control client session rejected."); }
        using var frameDeadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        frameDeadline.CancelAfter(TimeSpan.FromMilliseconds(500));
        var request = await ControlProtocol.ReadAsync<ControlRequest>(pipe, frameDeadline.Token).ConfigureAwait(false);
        ControlProtocol.Validate(request);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        deadline.CancelAfter(ControlProtocol.ExchangeTimeout);
        var response = await session.ExecuteAsync(request, deadline.Token).ConfigureAwait(false);
        await ControlProtocol.WriteAsync(pipe, response, deadline.Token).ConfigureAwait(false);
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        drain.CancelAfter(TimeSpan.FromMilliseconds(500));
        try
        {
            if (await pipe.ReadAsync(new byte[1], drain.Token).ConfigureAwait(false) != 0)
            { throw new InvalidDataException("One control exchange per connection required."); }
        }
        finally
        {
            if (request.Operation == ControlOperation.Shutdown && response.Ok) { shutdown.TrySetResult(); }
        }
    }

    public ValueTask DisposeAsync() => PipeListenerShutdown.CompleteAsync(stopping, listeners, pipes);
}
