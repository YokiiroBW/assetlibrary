using System.Security.AccessControl;
using System.Security.Principal;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialStatePermissions
{
    public static void RequirePrivate(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var sections = AccessControlSections.Access | AccessControlSections.Owner;
            FileSystemSecurity security = directory
                ? new DirectoryInfo(path).GetAccessControl(sections)
                : new FileInfo(path).GetAccessControl(sections);
            RequirePrivateWindows(security);
        }
        else if ((File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
        {
            throw new TrialConfigurationException("trial_state_permissions_unsafe");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RequirePrivateWindows(FileSystemSecurity security)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (identity.User is null || !identity.User.Equals(owner))
        {
            throw new TrialConfigurationException("trial_state_owner_mismatch");
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            var principal = (SecurityIdentifier)rule.IdentityReference;
            if (!principal.Equals(identity.User)
                && !principal.IsWellKnown(WellKnownSidType.LocalSystemSid)
                && !principal.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
            {
                throw new TrialConfigurationException("trial_state_permissions_unsafe");
            }
        }
    }
}
