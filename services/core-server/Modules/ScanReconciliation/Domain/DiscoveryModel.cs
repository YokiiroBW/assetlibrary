using AssetLibrary.Modules.AssetIdentity.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Domain;

[Flags]
public enum DiscoveryAttributes
{
    None = 0,
    Hidden = 1,
    System = 2,
    ReparsePoint = 4,
}

public sealed record DiscoveredEntry(
    RelativeAssetPath RelativePath,
    AssetEntryKind Kind,
    long? ContentLength,
    DateTimeOffset LastWriteTimeUtc,
    DiscoveryAttributes Attributes)
{
    public bool IsDirectory => Kind is AssetEntryKind.Directory or AssetEntryKind.ReparseDirectory;
}

public static class DefaultDiscoveryPolicy
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN",
        ".assetmeta",
        ".cache",
        ".Trashes",
        "@eaDir",
        "System Volume Information",
    };

    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
        "desktop.ini",
        "Thumbs.db",
    };

    public static bool ShouldInclude(DiscoveredEntry entry)
    {
        var name = entry.RelativePath.Name;
        if ((entry.Attributes & DiscoveryAttributes.System) != 0)
        {
            return false;
        }

        if (entry.IsDirectory)
        {
            return !IgnoredDirectoryNames.Contains(name)
                && !name.StartsWith(".Trash-", StringComparison.OrdinalIgnoreCase);
        }

        return !IgnoredFileNames.Contains(name)
            && !name.StartsWith("~$", StringComparison.Ordinal)
            && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldRecurse(DiscoveredEntry entry) =>
        entry.Kind == AssetEntryKind.Directory && ShouldInclude(entry);
}
