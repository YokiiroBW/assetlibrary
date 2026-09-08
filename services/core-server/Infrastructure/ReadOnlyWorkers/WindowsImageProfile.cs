using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

[SupportedOSPlatform("windows")]
internal sealed class WindowsImageProfile : IDisposable
{
    private readonly string record;
    private nint sid;
    private bool created;
    private bool disposed;
    public string Name { get; }
    public string DirectoryPath { get; }
    public string Executable => Path.Combine(DirectoryPath, "AssetLibrary.ImagePreview.Worker.exe");
    public nint Sid => sid;

    private WindowsImageProfile(string stateDirectory)
    {
        Name = "alimg." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stateDirectory);
        DirectoryPath = Directory.CreateTempSubdirectory("assetlibrary-image-worker-").FullName;
        record = Path.Combine(stateDirectory, Name + ".owner");
    }

    public static WindowsImageProfile Create(string workerExecutable, string stateDirectory)
    {
        var profile = new WindowsImageProfile(stateDirectory);
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User ?? throw new ReadOnlyWorkerException("preview_identity_unavailable");
            profile.ProtectDirectory(owner);
            using (var ownerRecord = new StreamWriter(new FileStream(profile.record, FileMode.CreateNew, FileAccess.Write, FileShare.None)))
            {
                ownerRecord.WriteLine(profile.Name);
                ownerRecord.WriteLine(profile.DirectoryPath);
            }
            var result = Native.CreateAppContainerProfile(profile.Name, profile.Name, "Temporary image decoding", nint.Zero, 0, out profile.sid);
            if (result != 0)
            {
                // A collision is never adopted: the existing profile belongs to somebody else.
                File.Delete(profile.record);
                throw new ReadOnlyWorkerException("preview_profile_creation_failed");
            }

            profile.created = true;
            File.Copy(workerExecutable, profile.Executable, overwrite: false);
            File.Copy(Path.Combine(Path.GetDirectoryName(workerExecutable)!, "libSkiaSharp.dll"),
                Path.Combine(profile.DirectoryPath, "libSkiaSharp.dll"), overwrite: false);
            var application = new SecurityIdentifier(profile.sid);
            var directory = new DirectoryInfo(profile.DirectoryPath);
            var acl = directory.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(application, FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
            return profile;
        }
        catch
        {
            profile.Dispose();
            throw;
        }
    }

    private void ProtectDirectory(SecurityIdentifier owner)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        foreach (var principal in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
        {
            security.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        new DirectoryInfo(DirectoryPath).SetAccessControl(security);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var removed = !created || Native.DeleteAppContainerProfile(Name) == 0;
        if (sid != nint.Zero)
        {
            _ = Native.FreeSid(sid);
            sid = nint.Zero;
        }

        if (removed)
        {
            Directory.Delete(DirectoryPath, recursive: true);
            File.Delete(record);
        }

        // On cleanup failure the private owner record is deliberately retained for bounded recovery.
        GC.SuppressFinalize(this);
        if (!removed) throw new ReadOnlyWorkerException("preview_profile_cleanup_failed");
    }

    private static class Native
    {
        [DllImport("userenv.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CreateAppContainerProfile(string name, string displayName, string description,
            nint capabilities, uint count, out nint sid);

        [DllImport("userenv.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int DeleteAppContainerProfile(string name);

        [DllImport("advapi32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern nint FreeSid(nint sid);
    }
}
