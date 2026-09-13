using AssetLibrary.ImagePreview.Isolation;

namespace AssetLibrary.ImagePreview.Worker;

internal sealed class ImageWorkerIsolation : IDisposable
{
    private readonly string mode;
    private readonly bool container;
    private LinuxImageProbe? processProbe;
    private LinuxContainerProbe? containerProbe;
    private ImageWorkerIsolation(string mode) { this.mode = mode; container = mode.Contains("container", StringComparison.Ordinal); }
    internal static ImageWorkerIsolation? Create(string[] args)
    {
        var mode = args.Length == 0 ? "" : args.Length == 1 ? args[0] : null;
        if (mode is not ("" or "--container-decoder" or "--probe-isolation" or "--probe-memory" or "--probe-cpu" or "--probe-threads"
            or "--probe-container-isolation" or "--probe-container-memory" or "--probe-container-cpu" or "--probe-container-threads")) return null;
        if (mode.Length != 0 && !OperatingSystem.IsLinux()) return null;
        var value = new ImageWorkerIsolation(mode);
        if (value.container && OperatingSystem.IsLinux() && !LinuxContainerIdentity.DropDecoderIdentity()) return null;
        return value;
    }
    internal void PrepareProbe()
    {
        if (!OperatingSystem.IsLinux() || !mode.StartsWith("--probe-", StringComparison.Ordinal)) return;
        if (container) containerProbe = LinuxContainerProbe.Prepare(mode);
        else processProbe = LinuxImageProbe.Prepare(mode);
    }
    internal bool Enter() => OperatingSystem.IsLinux()
        ? (container ? LinuxContainerIsolation.Enter() : LinuxImageIsolation.Enter())
        : OperatingSystem.IsWindows() && WindowsImageIsolation.IsEnforced();
    internal int? RunProbe(Stream output)
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (processProbe is not null) return processProbe.Run(output);
        return containerProbe?.Run(output);
    }
    public void Dispose() => processProbe?.Dispose();
}
