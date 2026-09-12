using System.IO.Pipes;

namespace AssetLibrary.Windows.AssetHost;

internal static class PipeListenerShutdown
{
    internal static async ValueTask CompleteAsync(CancellationTokenSource stopping, Task[] listeners, List<NamedPipeServerStream> pipes)
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(listeners).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        finally
        {
            foreach (var pipe in pipes) { pipe.Dispose(); }
            stopping.Dispose();
        }
    }
}
