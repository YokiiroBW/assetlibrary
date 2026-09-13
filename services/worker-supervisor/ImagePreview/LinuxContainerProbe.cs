using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetLibrary.ImagePreview.Isolation;

namespace AssetLibrary.ImagePreview.Worker;

[SupportedOSPlatform("linux")]
internal sealed class LinuxContainerProbe(string mode)
{
    private readonly int initialRingError = LinuxContainerIsolation.RingCreationError();
    internal static bool Accepts(string[] args) => args.Length == 1 && args[0] is "--probe-container-isolation"
        or "--probe-container-memory" or "--probe-container-cpu" or "--probe-container-threads";
    internal static LinuxContainerProbe Prepare(string mode)
    {
        var value = new LinuxContainerProbe(mode);
        _ = JsonSerializer.SerializeToUtf8Bytes(new ContainerProbeReport("warmup", false), ContainerProbeJson.Default.ContainerProbeReport);
        return value;
    }
    internal int Run(Stream output)
    {
        if (mode == "--probe-container-cpu") { while (true) Thread.SpinWait(10000); }
        var report = mode switch
        {
            "--probe-container-memory" => new ContainerProbeReport("memory", MemoryDenied()),
            "--probe-container-threads" => new ContainerProbeReport("threads", ThreadDenied()),
            _ => Isolation(),
        };
        output.Write(JsonSerializer.SerializeToUtf8Bytes(report, ContainerProbeJson.Default.ContainerProbeReport));
        output.Flush(); return report.Passed ? 0 : 1;
    }
    private ContainerProbeReport Isolation()
    {
        var ring = LinuxContainerIsolation.RingCreationError();
        var fork = LinuxContainerIsolation.DeniedFork();
        var parent = LinuxContainerIsolation.ParentSignalError();
        var stateDenied = false;
        try { using var file = File.OpenRead("/run/assetlibrary-image/supervisor-state.bin"); }
        catch (UnauthorizedAccessException) { stateDenied = true; }
        var identity = LinuxContainerIdentity.Decoder();
        return new ContainerProbeReport("isolation", initialRingError == 0 && ring == 12 && fork == 11 && parent == 1 && stateDenied && identity,
            initialRingError, ring, fork, parent, stateDenied, identity);
    }
    private static bool MemoryDenied()
    {
        nint memory = 0;
        try { memory = Marshal.AllocHGlobal(768 * 1024 * 1024); return false; }
        catch (OutOfMemoryException) { return true; }
        finally { if (memory != 0) Marshal.FreeHGlobal(memory); }
    }
    private static bool ThreadDenied()
    {
        var thread = new Thread(() => { });
        try { thread.Start(); thread.Join(); return false; }
        catch (Exception failure) when (failure is OutOfMemoryException or ThreadStartException) { return true; }
    }
}
internal sealed record ContainerProbeReport(string Probe, bool Passed, int InitialRingError = -1, int RingError = -1,
    int ForkError = -1, int ParentSignalError = -1, bool StateDenied = false, bool DecoderIdentity = false);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ContainerProbeReport))]
internal sealed partial class ContainerProbeJson : JsonSerializerContext;
