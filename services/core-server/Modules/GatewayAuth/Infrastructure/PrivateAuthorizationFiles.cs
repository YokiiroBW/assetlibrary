using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PrivateAuthorizationFiles
{
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OtherPermissions = UnixFileMode.GroupRead | UnixFileMode.GroupWrite
        | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public static void ValidateParent(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            throw Unavailable();
        }

        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
        {
            throw Unavailable();
        }

        for (var current = new DirectoryInfo(parent); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw Unavailable();
            }
        }

        if (OperatingSystem.IsWindows())
        {
            ValidateWindowsAccess(new DirectoryInfo(parent).GetAccessControl());
        }
        else if ((File.GetUnixFileMode(parent) & OtherPermissions) != 0)
        {
            throw Unavailable();
        }
    }

    public static void ValidateFile(string path)
    {
        ValidateParent(path);
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
        {
            throw Unavailable();
        }

        if (OperatingSystem.IsWindows())
        {
            ValidateWindowsAccess(new FileInfo(path).GetAccessControl());
        }
        else if ((File.GetUnixFileMode(path) & OtherPermissions) != 0)
        {
            throw Unavailable();
        }
    }

    public static FileStream Create(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        var stream = new FileStream(path, options);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                RestrictWindowsFile(path);
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateWindowsAccess(FileSystemSecurity security)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User ?? throw Unavailable();
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !Permitted(owner, user))
        {
            throw Unavailable();
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (rule.AccessControlType == AccessControlType.Allow
                && !Permitted(sid, user))
            {
                throw Unavailable();
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool Permitted(SecurityIdentifier sid, SecurityIdentifier user) =>
        sid.Equals(user) || sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
        || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);

    [SupportedOSPlatform("windows")]
    private static void RestrictWindowsFile(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User ?? throw Unavailable();
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static IOException Unavailable() => new("The gateway authorization storage is unavailable.");
}
