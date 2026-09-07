using System.Security.Cryptography;
using System.Text;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialPrivateState
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static FileStream AcquireInstance(string statePath)
    {
        var path = Path.Combine(statePath, ".read-only-trial.lock");
        RequireSafePath(path);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new TrialConfigurationException("trial_instance_already_running");
        }
    }

    public static async ValueTask<string> ReadTextAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        RequireSafePath(path);
        RequirePrivateFile(path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length > maximumBytes)
        {
            throw new TrialConfigurationException("trial_configuration_file_too_large");
        }

        var buffer = new byte[maximumBytes + 1];
        try
        {
            var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, deadline.Token)
                .ConfigureAwait(false);
            if (length > maximumBytes)
            {
                throw new TrialConfigurationException("trial_configuration_file_too_large");
            }

            return new UTF8Encoding(false, true).GetString(buffer, 0, length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    public static void RequireDirectory(string path)
    {
        RequireSafePath(path);
        if (!Directory.Exists(path))
        {
            throw new TrialConfigurationException("trial_state_directory_missing");
        }

        TrialStatePermissions.RequirePrivate(path, directory: true);
    }

    public static void RequireSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Any(char.IsControl))
        {
            throw new TrialConfigurationException("trial_configuration_path_invalid");
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (root is null || string.Equals(Path.TrimEndingDirectorySeparator(fullPath), Path.TrimEndingDirectorySeparator(root), PathComparison)
            || (OperatingSystem.IsWindows() && new DriveInfo(root).DriveType != DriveType.Fixed))
        {
            throw new TrialConfigurationException("trial_configuration_requires_local_storage");
        }

        var current = fullPath;
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new TrialConfigurationException("trial_configuration_reparse_point");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    public static void RequireContained(string parent, string path)
    {
        if (!Contains(parent, path) || string.Equals(Path.GetFullPath(parent), Path.GetFullPath(path), PathComparison))
        {
            throw new TrialConfigurationException("trial_configuration_outside_state");
        }
    }

    public static bool Overlaps(string left, string right) => Contains(left, right) || Contains(right, left);

    private static bool Contains(string parent, string path)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return normalizedPath.Equals(normalizedParent, PathComparison)
            || normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, PathComparison);
    }

    public static void RequirePrivateFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new TrialConfigurationException("trial_configuration_file_missing");
        }

        TrialStatePermissions.RequirePrivate(path, directory: false);
    }
}
