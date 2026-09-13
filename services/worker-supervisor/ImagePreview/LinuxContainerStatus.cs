using System.Globalization;
using System.Runtime.Versioning;

namespace AssetLibrary.ImagePreview.Isolation;

internal static class LinuxContainerStatus
{
    [SupportedOSPlatform("linux")]
    internal static bool AllThreads(string directory, uint identity, ulong capabilities)
    {
        var count = 0;
        foreach (var thread in Directory.EnumerateDirectories(directory))
        {
            if (++count > 256 || !Valid(ReadBounded(Path.Combine(thread, "status"), 8192), identity, capabilities)) return false;
        }
        return count > 0;
    }
    internal static bool Valid(string status, uint identity, ulong capabilities)
    {
        var fields = status.Split('\n').Where(line => line.Contains(':')).Select(line => line.Split(':', 2))
            .ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.Ordinal);
        return Ids(fields.GetValueOrDefault("Uid"), identity) && Ids(fields.GetValueOrDefault("Gid"), identity)
            && Hex(fields.GetValueOrDefault("CapEff"), capabilities) && Hex(fields.GetValueOrDefault("CapPrm"), capabilities)
            && Hex(fields.GetValueOrDefault("CapInh"), 0) && fields.GetValueOrDefault("NoNewPrivs") == "1";
    }
    private static bool Ids(string? text, uint expected)
    {
        var values = text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return values is { Length: 4 } && values.All(value => uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number == expected);
    }
    private static bool Hex(string? text, ulong expected) => ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) && value == expected;
    internal static string ReadBounded(string path, int maximum)
    {
        using var input = File.OpenRead(path); var bytes = new byte[maximum + 1];
        var read = input.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (read > maximum) throw new IOException("Kernel metadata limit exceeded.");
        return System.Text.Encoding.UTF8.GetString(bytes, 0, read);
    }
}
