using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal sealed record ImageSourcePath(string Anchor, IReadOnlyList<string> Components)
{
    public static ImageSourcePath Create(CanonicalLibraryRoot root, RelativeAssetPath relative)
    {
        // Revalidate default value objects at the physical boundary as well as normal callers.
        _ = new CanonicalLibraryRoot(root.Value, root.Comparison);
        _ = new RelativeAssetPath(relative.Value);
        string anchor;
        string suffix;
        if (OperatingSystem.IsWindows())
        {
            var nativeRoot = root.Value.Replace('/', '\\');
            anchor = Path.GetPathRoot(nativeRoot) ?? string.Empty;
            if (anchor.Length == 0 || nativeRoot.StartsWith("\\\\?", StringComparison.Ordinal)
                || nativeRoot.StartsWith("\\\\.", StringComparison.Ordinal)
                || !Path.IsPathFullyQualified(nativeRoot))
            {
                throw new ReadOnlyWorkerException("preview_source_unavailable");
            }

            suffix = nativeRoot[anchor.Length..].Replace('\\', '/');
            foreach (var component in anchor.Split(['\\', ':'], StringSplitOptions.RemoveEmptyEntries))
            {
                ValidateWindowsComponent(component);
            }
        }
        else if (OperatingSystem.IsLinux() && root.Value.StartsWith('/')
            && !root.Value.StartsWith("//", StringComparison.Ordinal))
        {
            anchor = "/";
            suffix = root.Value[1..];
        }
        else
        {
            throw new ReadOnlyWorkerException("preview_source_unavailable");
        }

        var components = (suffix.Length == 0 ? relative.Value : suffix + "/" + relative.Value).Split('/');
        if (components.Length > 128)
        {
            throw new ReadOnlyWorkerException("preview_source_limit");
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var component in components) ValidateWindowsComponent(component);
        }

        return new ImageSourcePath(anchor, components);
    }

    private static void ValidateWindowsComponent(string component)
    {
        var baseName = component.Split('.')[0];
        var reserved = baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
            || (baseName.Length == 4 && (baseName[3] is >= '0' and <= '9' or '¹' or '²' or '³')
                && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
        if (reserved || component.Length == 0 || component.EndsWith(' ') || component.EndsWith('.')
            || component.Any(value => value < ' ' || ":<>\"|?*".Contains(value, StringComparison.Ordinal)))
        {
            throw new ReadOnlyWorkerException("preview_source_unavailable");
        }
    }
}
