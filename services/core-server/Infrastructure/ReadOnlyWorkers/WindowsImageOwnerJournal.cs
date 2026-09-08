using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal enum WindowsImageOwnerPhase { Prepared, Created, Launching, Started, Removed }

[SupportedOSPlatform("windows")]
internal static class WindowsImageOwnerJournal
{
    public const string Version = "AssetLibrary.WindowsImageOwner.2";

    public static void Write(string record, string name, WindowsImageOwnerPhase phase, uint processId, long processCreation, ref bool recordOwned, ref bool temporaryOwned)
    {
        var text = string.Join('\n', Version, name, ((int)phase).ToString(CultureInfo.InvariantCulture),
            processId.ToString(CultureInfo.InvariantCulture), processCreation.ToString(CultureInfo.InvariantCulture)) + "\n";
        var temporary = record + ".tmp";
        if (temporaryOwned)
        {
            if ((File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
                throw new ReadOnlyWorkerException("preview_owner_record_invalid");
            File.Delete(temporary);
            temporaryOwned = false;
        }
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            temporaryOwned = true;
            file.Write(Encoding.UTF8.GetBytes(text));
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, record, overwrite: recordOwned);
        temporaryOwned = false;
        recordOwned = true;
    }

    public static (WindowsImageOwnerPhase Phase, uint Pid, long Creation) Read(string record, string name)
    {
        if ((File.GetAttributes(record) & FileAttributes.ReparsePoint) != 0)
            throw new ReadOnlyWorkerException("preview_owner_record_invalid");
        using var file = new FileStream(record, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > 512) throw new ReadOnlyWorkerException("preview_owner_record_invalid");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        var fields = Encoding.UTF8.GetString(bytes).Split('\n');
        if (fields.Length != 6 || fields[0] != Version || fields[1] != name || fields[5].Length != 0
            || !int.TryParse(fields[2], CultureInfo.InvariantCulture, out var phase) || phase is < 0 or > 4
            || !uint.TryParse(fields[3], CultureInfo.InvariantCulture, out var pid)
            || !long.TryParse(fields[4], CultureInfo.InvariantCulture, out var creation)
            || creation < 0
            || fields[2] != phase.ToString(CultureInfo.InvariantCulture)
            || fields[3] != pid.ToString(CultureInfo.InvariantCulture)
            || fields[4] != creation.ToString(CultureInfo.InvariantCulture)
            || (phase == (int)WindowsImageOwnerPhase.Started && (pid == 0 || creation == 0)))
            throw new ReadOnlyWorkerException("preview_owner_record_invalid");
        return ((WindowsImageOwnerPhase)phase, pid, creation);
    }

    public static string PrepareRoot(string stateDirectory)
    {
        var directory = Path.GetFullPath(Path.Combine(stateDirectory, "windows-image-owners-v2"));
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        var marker = Path.Combine(directory, ".format");
        if (Native.CreateDirectoryW(directory, nint.Zero))
        {
            using var identity = WindowsIdentity.GetCurrent();
            Protect(new DirectoryInfo(directory), identity.User ?? throw new ReadOnlyWorkerException("preview_identity_unavailable"));
            File.WriteAllText(marker, Version);
        }
        else
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new ReadOnlyWorkerException("preview_owner_root_reparse");
            // Another permitted startup may be publishing the private root marker.
            for (var attempt = 0; attempt < 3 && !File.Exists(marker); attempt++) Thread.Sleep(20);
            if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0
                || !HasRootMarker(marker))
                throw new ReadOnlyWorkerException("preview_owner_root_unrecognized");
        }
        return directory;
    }

    private static bool HasRootMarker(string marker)
    {
        using var input = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[Encoding.UTF8.GetByteCount(Version)];
        if (input.Length != bytes.Length) return false;
        input.ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes) == Version;
    }

    public static void Protect(DirectoryInfo directory, SecurityIdentifier owner)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var principal in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
        {
            security.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        directory.SetAccessControl(security);
        // Object owners have WRITE_DAC even when the inherited ACL grants only Modify.
        // Requesting WRITE_OWNER in that first update can fail before the private DACL is applied.
        var current = directory.GetAccessControl();
        if (!owner.Equals(current.GetOwner(typeof(SecurityIdentifier))))
        {
            current.SetOwner(owner);
            directory.SetAccessControl(current);
            current = directory.GetAccessControl();
        }
        if (!current.AreAccessRulesProtected || !owner.Equals(current.GetOwner(typeof(SecurityIdentifier))))
            throw new ReadOnlyWorkerException("preview_owner_acl_unverified");
    }

    public static FileStream CreateLease(string root, string name)
        => new(Path.Combine(root, name + ".lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

    public static FileStream? OpenRecoveryLease(string root, string name)
    {
        try { return new FileStream(Path.Combine(root, name + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException failure) when ((failure.HResult & 0xffff) == 32) { return null; }
    }

    public static string[] EnumerateRecords(string root)
        => Directory.EnumerateFiles(root, "alimg2.*.owner", SearchOption.TopDirectoryOnly).Take(65).ToArray();

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateDirectoryW(string path, nint attributes);
    }
}
