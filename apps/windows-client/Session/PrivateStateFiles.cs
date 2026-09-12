using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AssetLibrary.Windows.Session;

[SupportedOSPlatform("windows")]
internal sealed class PrivateStateFiles(string directory)
{
    internal void EnsureDirectory()
    {
        var info = new DirectoryInfo(directory);
        for (var ancestor = info; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Exists && ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))
            { throw new UnauthorizedAccessException("Connection directory cannot be a link."); }
        }
        if (!info.Exists)
        {
            var sid = new SecurityIdentifier(LocalPipe.UserSid);
            var security = new DirectorySecurity();
            security.SetOwner(sid);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            info.Create(security);
        }
        Verify(info);
    }

    internal void Delete(string name)
    {
        EnsureDirectory();
        var path = Path.Combine(directory, name);
        if (File.Exists(path)) { Verify(new FileInfo(path)); File.Delete(path); }
    }

    private static void Verify(FileSystemInfo info)
    {
        info.Refresh();
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) { throw new UnauthorizedAccessException("Connection state cannot be a link."); }
        FileSystemSecurity security = info is DirectoryInfo directoryInfo
            ? directoryInfo.GetAccessControl() : ((FileInfo)info).GetAccessControl();
        var sid = new SecurityIdentifier(LocalPipe.UserSid);
        if (!sid.Equals(security.GetOwner(typeof(SecurityIdentifier)))) { throw new UnauthorizedAccessException("Connection state owner mismatch."); }
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow && !sid.Equals(rule.IdentityReference))
            { throw new UnauthorizedAccessException("Connection state permissions are not private."); }
        }
    }

    internal async Task<byte[]?> ReadAsync(string name, CancellationToken token)
    {
        var info = new FileInfo(Path.Combine(directory, name));
        if (!info.Exists) { return null; }
        Verify(info);
        await using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is < 2 or > 32768) { throw new InvalidDataException("Invalid connection state size."); }
        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return bytes;
    }

    internal async Task WriteAsync(string name, byte[] bytes, CancellationToken token)
    {
        var path = Path.Combine(directory, name);
        if (File.Exists(path)) { Verify(new FileInfo(path)); }
        var temporary = Path.Combine(directory, ".pending-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var sid = new SecurityIdentifier(LocalPipe.UserSid);
                var security = new FileSecurity();
                security.SetOwner(sid);
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
                new FileInfo(temporary).SetAccessControl(security);
                Verify(new FileInfo(temporary));
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}
