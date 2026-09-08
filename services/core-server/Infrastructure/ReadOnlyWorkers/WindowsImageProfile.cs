using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

[SupportedOSPlatform("windows")]
internal sealed class WindowsImageProfile : IDisposable
{
    private const string Prefix = "alimg2.";
    private readonly string root;
    private readonly string record;
    private readonly string leasePath;
    private FileStream? lease;
    private WindowsImageOwnerPhase phase;
    private uint processId;
    private long processCreation;
    private bool recordOwned;
    private bool directoryOwned;
    private bool temporaryOwned;
    private bool leaseOwned;
    private nint sid;
    private bool disposed;
    public string Name { get; }
    public string DirectoryPath { get; }
    public string Executable => Path.Combine(DirectoryPath, "AssetLibrary.ImagePreview.Worker.exe");
    public nint Sid => sid;

    private WindowsImageProfile(string stateDirectory)
    {
        root = WindowsImageOwnerJournal.PrepareRoot(stateDirectory);
        Name = Prefix + Guid.NewGuid().ToString("N");
        DirectoryPath = Path.Combine(root, Name);
        record = Path.Combine(root, Name + ".owner");
        leasePath = Path.Combine(root, Name + ".lease");
    }

    private WindowsImageProfile(string root, string name, FileStream lease, WindowsImageOwnerPhase phase, uint pid, long creation)
    {
        this.root = root;
        Name = name;
        DirectoryPath = Path.Combine(root, name);
        record = Path.Combine(root, name + ".owner");
        leasePath = Path.Combine(root, name + ".lease");
        this.lease = lease;
        leaseOwned = true;
        this.phase = phase;
        processId = pid;
        processCreation = creation;
        recordOwned = true;
        directoryOwned = true;
        temporaryOwned = File.Exists(record + ".tmp");
    }

    public static WindowsImageProfile Create(string workerExecutable, string stateDirectory, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        _ = Recover(stateDirectory, token);
        var profile = new WindowsImageProfile(stateDirectory);
        try
        {
            profile.lease = WindowsImageOwnerJournal.CreateLease(profile.root, profile.Name);
            profile.leaseOwned = true;
            if (!Native.CreateDirectoryW(profile.DirectoryPath, nint.Zero))
                throw new ReadOnlyWorkerException("preview_profile_collision", Marshal.GetLastPInvokeError());
            profile.directoryOwned = true;
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User ?? throw new ReadOnlyWorkerException("preview_identity_unavailable");
            WindowsImageOwnerJournal.Protect(new DirectoryInfo(profile.DirectoryPath), owner);
            profile.WriteRecord();
            token.ThrowIfCancellationRequested();
            var result = Native.CreateAppContainerProfile(profile.Name, profile.Name, "Temporary image decoding", nint.Zero, 0, out profile.sid);
            if (result != 0)
            {
                // A collision is never adopted: the existing profile belongs to somebody else.
                throw new ReadOnlyWorkerException("preview_profile_creation_failed", result);
            }

            profile.phase = WindowsImageOwnerPhase.Created;
            profile.WriteRecord();
            token.ThrowIfCancellationRequested();
            WindowsImageArtifact.Copy(workerExecutable, profile.Executable, token);
            WindowsImageArtifact.Copy(Path.Combine(Path.GetDirectoryName(workerExecutable)!, "libSkiaSharp.dll"),
                Path.Combine(profile.DirectoryPath, "libSkiaSharp.dll"), token);
            var application = new SecurityIdentifier(profile.sid);
            var directory = new DirectoryInfo(profile.DirectoryPath);
            var acl = directory.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(application, FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
            token.ThrowIfCancellationRequested();
            return profile;
        }
        catch (Exception failure)
        {
            try { profile.Dispose(); }
            catch (Exception cleanup) { PreserveCleanupFailure(failure, cleanup); }
            throw;
        }
    }

    public void BeginLaunch()
    {
        phase = WindowsImageOwnerPhase.Launching;
        WriteRecord();
    }

    public void RecordProcess(uint pid, nint handle)
    {
        processCreation = WindowsImageProcessIdentity.CreationTime(handle);
        processId = pid;
        phase = WindowsImageOwnerPhase.Started;
        WriteRecord();
    }

    private void WriteRecord() => WindowsImageOwnerJournal.Write(record, Name, phase, processId, processCreation, ref recordOwned, ref temporaryOwned);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var completed = false;
        try
        {
            if (phase is WindowsImageOwnerPhase.Launching or WindowsImageOwnerPhase.Started && !WindowsImageProcessIdentity.HasExited(processId, processCreation, sid))
                throw new ReadOnlyWorkerException("preview_profile_process_active");
            if (phase is >= WindowsImageOwnerPhase.Created and < WindowsImageOwnerPhase.Removed)
            {
                var result = Native.DeleteAppContainerProfile(Name);
                if (result != 0) throw new ReadOnlyWorkerException("preview_profile_cleanup_failed", result);
                phase = WindowsImageOwnerPhase.Removed;
                WriteRecord();
            }
            RemoveDirectory();
            if (recordOwned) File.Delete(record);
            if (temporaryOwned) File.Delete(record + ".tmp");
            completed = true;
        }
        finally
        {
            ReleaseForRecovery();
            if (completed && leaseOwned) File.Delete(leasePath);
            GC.SuppressFinalize(this);
        }
    }

    internal void ReleaseForRecovery()
    {
        if (sid != nint.Zero) _ = Native.FreeSid(sid);
        sid = nint.Zero;
        lease?.Dispose();
        lease = null;
    }

    private void RemoveDirectory()
    {
        if (!directoryOwned || !Directory.Exists(DirectoryPath)) return;
        if (!DirectoryPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || (File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0)
            throw new ReadOnlyWorkerException("preview_profile_cleanup_boundary");
        foreach (var entry in new DirectoryInfo(DirectoryPath).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new ReadOnlyWorkerException("preview_profile_cleanup_boundary");
        }
        Directory.Delete(DirectoryPath, recursive: true);
    }

    internal static void PreserveCleanupFailure(Exception original, Exception cleanup)
    {
        original.Data["windows_image_cleanup_code"] = cleanup is ReadOnlyWorkerException worker
            ? worker.Code : "preview_profile_cleanup_failed";
        if (cleanup is ReadOnlyWorkerException { NativeError: { } error })
            original.Data["windows_image_cleanup_native_error"] = error;
    }

    public static int Recover(string stateDirectory, CancellationToken token = default)
    {
        var root = WindowsImageOwnerJournal.PrepareRoot(stateDirectory);
        var records = WindowsImageOwnerJournal.EnumerateRecords(root);
        if (records.Length > 64) throw new ReadOnlyWorkerException("preview_owner_recovery_limit");
        var recovered = 0;
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(record);
            if (name.Length != Prefix.Length + 32 || !Guid.TryParseExact(name[Prefix.Length..], "N", out _))
                throw new ReadOnlyWorkerException("preview_owner_record_invalid");
            var ownerLease = WindowsImageOwnerJournal.OpenRecoveryLease(root, name);
            if (ownerLease is null) continue;
            using (ownerLease)
            {
                var data = WindowsImageOwnerJournal.Read(record, name);
                if (data.Phase == WindowsImageOwnerPhase.Prepared) throw new ReadOnlyWorkerException("preview_owner_creation_unconfirmed");
                var profile = new WindowsImageProfile(root, name, ownerLease, data.Phase, data.Pid, data.Creation);
                if (profile.phase != WindowsImageOwnerPhase.Removed)
                {
                    var result = Native.DeriveAppContainerSidFromAppContainerName(name, out profile.sid);
                    if (result != 0)
                    {
                        if (profile.sid != nint.Zero) _ = Native.FreeSid(profile.sid);
                        throw new ReadOnlyWorkerException("preview_owner_sid_unavailable", result);
                    }
                }
                profile.Dispose();
                recovered++;
            }
        }
        return recovered;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateDirectoryW(string path, nint attributes);

        [DllImport("userenv.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int DeriveAppContainerSidFromAppContainerName(string name, out nint sid);

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
