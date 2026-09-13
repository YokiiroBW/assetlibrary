using System.Globalization;
using System.Runtime.Versioning;

namespace AssetLibrary.ImagePreview.Isolation;

internal static class LinuxContainerMemory
{
    [SupportedOSPlatform("linux")]
    internal static bool IsBounded()
    {
        var groups = LinuxContainerStatus.ReadBounded("/proc/self/cgroup", 8192).Split('\n');
        var mounts = LinuxContainerStatus.ReadBounded("/proc/self/mountinfo", 65536).Split('\n');
        foreach (var line in groups)
        {
            var group = line.Split(':', 3);
            if (group.Length != 3) continue;
            var unified = group[0] == "0" && group[1].Length == 0;
            if (!unified && !group[1].Split(',').Contains("memory", StringComparer.Ordinal)) continue;
            foreach (var mount in mounts)
            {
                var halves = mount.Split(" - ", 2, StringSplitOptions.None);
                if (halves.Length != 2) continue;
                var left = halves[0].Split(' '); var right = halves[1].Split(' ');
                if (left.Length < 6 || right.Length < 3 || right[0] != (unified ? "cgroup2" : "cgroup")) continue;
                if (!unified && !right[2].Split(',').Contains("memory", StringComparer.Ordinal)) continue;
                var path = Resolve(left[3], left[4], group[2]);
                if (path is null) continue;
                var value = LinuxContainerStatus.ReadBounded(Path.Combine(path, unified ? "memory.max" : "memory.limit_in_bytes"), 128).Trim();
                return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit is > 0 and <= 536870912;
            }
        }
        return false;
    }
    internal static string? Resolve(string root, string mount, string membership)
    {
        if (!(mount == "/sys/fs/cgroup" || mount.StartsWith("/sys/fs/cgroup/", StringComparison.Ordinal)) || mount.Contains((char)92) || membership.Split('/').Contains("..", StringComparer.Ordinal)) return null;
        if (root == membership) return mount;
        var prefix = root == "/" ? "/" : root.TrimEnd('/') + "/";
        return membership.StartsWith(prefix, StringComparison.Ordinal) ? mount.TrimEnd('/') + "/" + membership[prefix.Length..] : null;
    }
}
