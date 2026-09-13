using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.Versioning;
using AssetLibrary.ImagePreview.Protocol;

namespace AssetLibrary.ImageSupervisor;

[SupportedOSPlatform("linux")]
internal sealed class ImageSocketServer(Socket listener, ImageCircuitLedger circuit)
{
    private int active;
    private ImageNamespaceFailure? fatal;
    private bool stateFailed;
    internal async Task RunAsync(CancellationTokenSource stopping)
    {
        var requests = new List<Task>(2);
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptAsync(stopping.Token).ConfigureAwait(false);
                if (!LinuxImagePeer.IsCore(client)) { client.Dispose(); continue; }
                requests.RemoveAll(task => task.IsCompleted);
                // At most one decoder and one prior bounded publication; busy clients never form a task queue.
                if (requests.Count == 2 || circuit.Open || Interlocked.CompareExchange(ref active, 1, 0) != 0)
                { await RejectAsync(client, stopping.Token).ConfigureAwait(false); continue; }
                requests.Add(Task.Run(() => ServeAsync(client, stopping), CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { /* Normal owned shutdown. */ }
        finally { listener.Dispose(); await Task.WhenAll(requests).ConfigureAwait(false); }
        if (fatal is not null) throw fatal;
        if (stateFailed) throw new ImageStateFailure();
    }
    private async Task ServeAsync(Socket socket, CancellationTokenSource stopping)
    {
        using var client = new NetworkStream(socket, ownsSocket: true);
        var released = 0;
        void Release() { if (Interlocked.Exchange(ref released, 1) == 0) Interlocked.Exchange(ref active, 0); }
        try { await new ImageExchange(circuit, Release).RunAsync(client, stopping.Token).ConfigureAwait(false); }
        catch (ImageStateFailure)
        {
            stateFailed = true;
            await stopping.CancelAsync().ConfigureAwait(false);
        }
        catch (ImageNamespaceFailure failure)
        {
            fatal = failure;
            await stopping.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or Win32Exception or OperationCanceledException)
        { /* No unbounded diagnostics or retry. The pre-launch persistent reservation remains if recovery was incomplete. */ }
        finally { Release(); }
    }
    private static async Task RejectAsync(Socket socket, CancellationToken stopping)
    {
        using var client = new NetworkStream(socket, ownsSocket: true);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(500));
        try { await LocalImageFrames.WriteEmptyAsync(client, ImageWorkerStatus.Unavailable, deadline.Token).ConfigureAwait(false); }
        catch (Exception failure) when (failure is IOException or OperationCanceledException) { /* Busy reply is bounded and optional. */ }
    }
}
