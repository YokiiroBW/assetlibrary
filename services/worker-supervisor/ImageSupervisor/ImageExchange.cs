using System.Runtime.Versioning;
using AssetLibrary.ImagePreview.Protocol;

namespace AssetLibrary.ImageSupervisor;

[SupportedOSPlatform("linux")]
internal sealed class ImageExchange(ImageCircuitLedger circuit, Action release)
{
    internal async Task RunAsync(Stream client, CancellationToken stopping)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        ImageReply reply;
        try { circuit.Begin(); }
        catch (IOException) { throw new ImageStateFailure(); }
        using var child = await ImageChild.StartAsync("--container-decoder", deadline.Token).ConfigureAwait(false);
        using var monitor = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        Task disconnected = Task.CompletedTask;
        using var diagnostics = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var diagnostic = DrainErrorsAsync(child.Errors, diagnostics.Token);
        var infrastructureFailed = true;
        try
        {
            var ready = await LocalImageFrames.ReadAsync(child.Output, deadline.Token).ConfigureAwait(false);
            if (!LocalImageFrames.Empty(ready, ImageWorkerStatus.Ready)) throw new InvalidDataException("Image child is not ready.");
            await LocalImageFrames.WriteEmptyAsync(client, ImageWorkerStatus.Ready, deadline.Token).ConfigureAwait(false);
            var request = await ForwardInputAsync(client, child, deadline.Token).ConfigureAwait(false);
            disconnected = WatchDisconnectAsync(client, deadline, monitor.Token);
            reply = await ReadResultAsync(child, request.Profile, deadline.Token).ConfigureAwait(false);
            await diagnostic.ConfigureAwait(false);
            infrastructureFailed = reply.Frame.Status == (int)ImageWorkerStatus.Unavailable;
        }
        catch (OperationCanceledException) { reply = ImageReply.Unavailable; infrastructureFailed = false; }
        catch (ImageClientRejected) { reply = ImageReply.Unavailable; infrastructureFailed = false; }
        catch (Exception failure) when (failure is IOException or InvalidDataException) { reply = ImageReply.Unavailable; }
        finally
        {
            await monitor.CancelAsync().ConfigureAwait(false);
            await disconnected.ConfigureAwait(false);
            await child.StopAsync().ConfigureAwait(false);
            await diagnostics.CancelAsync().ConfigureAwait(false);
            try { await diagnostic.ConfigureAwait(false); }
            catch (Exception failure) when (failure is IOException or InvalidDataException or OperationCanceledException) { /* Already bounded, terminated and reaped. */ }
        }
        if (!infrastructureFailed)
        {
            try { circuit.CompleteHealthy(); }
            catch (IOException) { throw new ImageStateFailure(); }
        }
        // The prior decoder is gone before the client can receive a complete result and submit its next request.
        release();
        var publication = deadline;
        await client.WriteAsync(ImageWorkerProtocol.Header(reply.Frame.Status, reply.Frame.Profile, reply.Frame.Length,
            reply.Frame.Width, reply.Frame.Height), publication.Token).ConfigureAwait(false);
        await client.WriteAsync(reply.Bytes, publication.Token).ConfigureAwait(false);
    }
    private static async Task<ImageWorkerHeader> ForwardInputAsync(Stream client, ImageChild child, CancellationToken token)
    {
        try
        {
            var request = await LocalImageFrames.ReadAsync(client, token).ConfigureAwait(false);
            LocalImageFrames.RequireRequest(request);
            await child.Input.WriteAsync(ImageWorkerProtocol.Header(request.Status, request.Profile, request.Length), token).ConfigureAwait(false);
            await LocalImageFrames.CopyExactlyAsync(client, child.Input, request.Length, token).ConfigureAwait(false);
            await child.Input.FlushAsync(token).ConfigureAwait(false); child.Input.Dispose();
            return request;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException) { throw new ImageClientRejected(); }
    }

    private static async Task<ImageReply> ReadResultAsync(ImageChild child, int profile, CancellationToken token)
    {
        var frame = await LocalImageFrames.ReadAsync(child.Output, token).ConfigureAwait(false);
        byte[] png = [];
        if (frame.Status == (int)ImageWorkerStatus.Success)
        {
            LocalImageFrames.RequireOutput(frame, profile); png = new byte[frame.Length];
            await child.Output.ReadExactlyAsync(png, token).ConfigureAwait(false); LocalImageFrames.RequirePng(png, frame);
        }
        else if (!LocalImageFrames.IsImageFailure(frame)) throw new InvalidDataException("Unexpected child failure frame.");
        if (await child.Output.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0) throw new InvalidDataException("Unexpected child trailing output.");
        await child.ReapAsync(terminate: false, token).ConfigureAwait(false);
        if (child.ExitCode != (frame.Status == (int)ImageWorkerStatus.Success ? 0 : 1)) throw new InvalidDataException("Child exit disagrees with response.");
        return new ImageReply(frame, png);
    }
    private static async Task WatchDisconnectAsync(Stream client, CancellationTokenSource operation, CancellationToken monitor)
    {
        try { _ = await client.ReadAsync(new byte[1], monitor).ConfigureAwait(false); }
        catch (OperationCanceledException) when (monitor.IsCancellationRequested) { return; }
        catch (IOException) { /* Socket failure is cancellation, never an additional request. */ }
        await operation.CancelAsync().ConfigureAwait(false);
    }
    internal static async Task DrainErrorsAsync(Stream errors, CancellationToken token)
    {
        var bytes = new byte[1024]; var count = 0;
        while (true)
        {
            var read = await errors.ReadAsync(bytes, token).ConfigureAwait(false);
            if (read == 0) return;
            count += read;
            if (count > 4096) throw new InvalidDataException("Image diagnostics exceed the bound.");
        }
    }
}
internal sealed record ImageReply(ImageWorkerHeader Frame, byte[] Bytes)
{
    internal static ImageReply Unavailable => new(new ImageWorkerHeader(7, 0, 0, 0, 0), []);
}

internal sealed class ImageClientRejected : IOException;

internal sealed class ImageStateFailure : IOException;
