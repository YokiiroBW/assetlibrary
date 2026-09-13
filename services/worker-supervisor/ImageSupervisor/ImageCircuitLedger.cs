using System.Runtime.Versioning;

namespace AssetLibrary.ImageSupervisor;

[SupportedOSPlatform("linux")]
internal sealed class ImageCircuitLedger : IDisposable
{
    private readonly FileStream file;
    private readonly ImageCircuitState state;
    internal bool Open => state.Open;
    internal ImageCircuitLedger()
    {
        file = LinuxSupervisorFiles.OpenGuard();
        try
        {
            file.Lock(0, 8);
            LinuxSupervisorFiles.RecoverTemporary();
            var fresh = !File.Exists(LinuxSupervisorFiles.StatePath);
            using var existing = LinuxSupervisorFiles.OpenState(write: true);
            if (fresh && existing.Length == 0) { state = new ImageCircuitState(); Persist(); }
            else { var bytes = new byte[8]; existing.ReadExactly(bytes); state = ImageCircuitState.Read(bytes); }
        }
        catch { file.Dispose(); throw; }
    }
    internal void Begin()
    {
        // Reserve before launch: an abrupt PID1/container death leaves a durable failed attempt.
        state.ReserveAttempt(); Persist();
    }
    internal void CompleteHealthy() { state.CompleteHealthy(); Persist(); }
    private void Persist()
    {
        using (var next = LinuxSupervisorFiles.CreateNext()) { next.Write(state.Encode()); next.Flush(flushToDisk: true); }
        LinuxSupervisorFiles.CommitNext();
    }
    public void Dispose() => file.Dispose();
    internal static bool Healthy()
    {
        using var file = LinuxSupervisorFiles.OpenState(write: false);
        var bytes = new byte[8]; file.ReadExactly(bytes);
        return !ImageCircuitState.Read(bytes).Open;
    }
}
