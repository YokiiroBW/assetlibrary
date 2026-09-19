namespace AssetLibrary.Modules.OperationTrash.Domain;

/// <summary>
/// Pure POSIX relative-path algebra for manifest paths. Every method works on the raw manifest string:
/// a path is rejected before any normalization, so a hostile spelling can never be normalized into a
/// safe one. Windows device names are refused because the package layout is materialized on Windows
/// and NAS shares alike.
/// </summary>
public static class MediaPackagePathPolicy
{
    public const int MaximumPathLength = 240;
    public const int MaximumSegments = 2;

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// Returns the failure code for one raw manifest path, or null when the path is acceptable.
    /// </summary>
    public static string? Validate(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaximumPathLength)
        {
            return "invalid_path";
        }

        if (path[0] == '/')
        {
            return "invalid_path";
        }

        // A colon covers alternative data streams, drive letters and UNC spellings in one rule.
        if (path.Contains('\\') || path.Contains(':'))
        {
            return "invalid_path";
        }

        if (HasControlCharacter(path))
        {
            return "invalid_path";
        }

        var segments = path.Split('/');
        if (segments.Length > MaximumSegments)
        {
            return "invalid_path";
        }

        foreach (var segment in segments)
        {
            var invalid = segment.Length == 0
                || segment is "." or ".."
                || segment[0] == ' '
                || segment[^1] == ' '
                || segment[^1] == '.'
                || IsReservedDeviceName(segment);
            if (invalid)
            {
                return "invalid_path";
            }
        }

        return null;
    }

    /// <summary>
    /// Case-folded key used to detect packages that collide on case-insensitive volumes.
    /// </summary>
    public static string FoldKey(string path) => path.ToUpperInvariant();

    /// <summary>
    /// True when the two raw paths denote the same location once case and directory prefixes are
    /// folded. Both a duplicate file and a file/directory prefix collision are unsafe.
    /// </summary>
    public static bool Collides(string left, string right)
    {
        var foldedLeft = FoldKey(left);
        var foldedRight = FoldKey(right);
        if (string.Equals(foldedLeft, foldedRight, StringComparison.Ordinal))
        {
            return true;
        }

        return foldedLeft.StartsWith(foldedRight + "/", StringComparison.Ordinal)
            || foldedRight.StartsWith(foldedLeft + "/", StringComparison.Ordinal);
    }

    private static bool HasControlCharacter(string value) =>
        value.Any(character => char.IsControl(character) || character == '\u007f');

    private static bool IsReservedDeviceName(string segment)
    {
        var separator = segment.IndexOf('.');
        var stem = separator < 0 ? segment : segment[..separator];
        return ReservedDeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
}
