using System.Runtime.Versioning;

namespace AssetLibrary.ImageSupervisor;

[SupportedOSPlatform("linux")]
internal static class SupervisorProbe
{
    internal static async Task<int> RunAsync(string mode, ImageCircuitLedger circuit)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        if (circuit.Open) return 3;
        circuit.Begin();
        using var child = await ImageChild.StartAsync(mode, deadline.Token).ConfigureAwait(false);
        child.Input.Dispose();
        var errors = ImageExchange.DrainErrorsAsync(child.Errors, deadline.Token);
        var output = new byte[4097]; var count = 0;
        try
        {
            while (true)
            {
                var read = await child.Output.ReadAsync(output.AsMemory(count), deadline.Token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
                if (count == output.Length) throw new InvalidDataException("Probe output limit exceeded.");
            }
            await child.ReapAsync(terminate: false, deadline.Token).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            if (child.ExitCode == 0) circuit.CompleteHealthy();
            using var stdout = Console.OpenStandardOutput(); await stdout.WriteAsync(output.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
            return child.ExitCode;
        }
        finally
        {
            await child.StopAsync().ConfigureAwait(false);
            await deadline.CancelAsync().ConfigureAwait(false);
            try { await errors.ConfigureAwait(false); }
            catch (Exception failure) when (failure is IOException or InvalidDataException or OperationCanceledException) { /* Owned process is already reaped. */ }
        }
    }
}
