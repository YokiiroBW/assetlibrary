using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.ReadCore.Tests;

namespace AssetLibrary.Preview.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsImageDirectoryTests
{
    [TestMethod]
    public void OwnedDirectoryWithInheritedModifyCanBecomeStrictlyPrivate()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows ACL platform is required."); return; }
        using var sandbox = new RepositorySandbox();
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var parent = Directory.CreateDirectory(Path.Combine(sandbox.Root, "modify-parent"));
        var access = new DirectorySecurity();
        access.SetAccessRuleProtection(true, false);
        access.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        access.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        parent.SetAccessControl(access);
        // Elevated Windows runners may otherwise assign the Administrators group as owner.
        // Create this explicitly owned fixture with the current SID while retaining inherited ACLs.
        var child = new DirectoryInfo(Path.Combine(parent.FullName, "owned"));
        var childOwner = new DirectorySecurity();
        childOwner.SetOwner(user);
        child.Create(childOwner);
        Assert.AreEqual(user, child.GetAccessControl().GetOwner(typeof(SecurityIdentifier)));
        WindowsImageOwnerJournal.Protect(child, user);
        var result = child.GetAccessControl();
        Assert.IsTrue(result.AreAccessRulesProtected);
        Assert.AreEqual(user, result.GetOwner(typeof(SecurityIdentifier)));
        var rules = result.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Assert.HasCount(2, rules);
        Assert.IsTrue(rules.All(rule => !rule.IsInherited && rule.AccessControlType == AccessControlType.Allow
            && rule.FileSystemRights == FileSystemRights.FullControl));
        Assert.IsTrue(rules.Any(rule => rule.IdentityReference.Equals(user)));
        Assert.IsTrue(rules.Any(rule => rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null))));
    }
}
