using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal sealed class ThumbnailDecodeStartup : IDisposable
{
    private readonly List<IntPtr> owned = [];
    private IntPtr attributes;
    private bool initialized;
    internal ThumbnailDecodeStartup(IntPtr job, IntPtr[] inheritedPipes)
    {
        try
        {
            nuint length = 0;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref length);
            if (length is 0 or > 65536) { throw new IOException("Invalid thumbnail startup size."); }
            attributes = Allocate((int)length);
            if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref length)) { throw new IOException("Thumbnail startup failed."); }
            initialized = true;
            var handles = Allocate(inheritedPipes.Length * IntPtr.Size);
            Marshal.Copy(inheritedPipes, 0, handles, inheritedPipes.Length);
            AddAttribute(0x20002, handles, inheritedPipes.Length * IntPtr.Size);
            var jobList = Allocate(IntPtr.Size); Marshal.WriteIntPtr(jobList, job);
            AddAttribute(0x2000d, jobList, IntPtr.Size);
        }
        catch { Dispose(); throw; }
    }

    internal SafeProcessHandle Start(string executable, IntPtr[] pipes)
    {
        var startup = new Startup
        {
            Size = 112, Flags = 0x100, Input = pipes[0], Output = pipes[1], Error = pipes[2], Attributes = attributes,
        };
        var environment = Marshal.StringToHGlobalUni(EnvironmentBlock());
        try
        {
            if (!CreateProcess(executable, ('"' + executable + "\" --decode-thumbnail\0").ToCharArray(), IntPtr.Zero, IntPtr.Zero,
                true, 0x08080400, environment, Path.GetDirectoryName(executable)!, ref startup, out var child))
            { throw new IOException("Thumbnail process creation failed."); }
            using var thread = new SafeFileHandle(child.ThreadHandle, true);
            return new SafeProcessHandle(child.ProcessHandle, true);
        }
        finally { Marshal.FreeHGlobal(environment); }
    }

    private static string EnvironmentBlock()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent?.Parent?.Parent?.FullName
            ?? throw new IOException("Runtime location unavailable.");
        return "DOTNET_EnableDiagnostics=0\0DOTNET_ROOT=" + runtime + "\0SystemRoot=" + windows + "\0WINDIR=" + windows + "\0\0";
    }
    private IntPtr Allocate(int bytes) { var value = Marshal.AllocHGlobal(bytes); owned.Add(value); return value; }
    private void AddAttribute(nuint key, IntPtr value, int length)
    {
        if (!UpdateProcThreadAttribute(attributes, 0, key, value, (nuint)length, IntPtr.Zero, IntPtr.Zero))
        { throw new IOException("Thumbnail startup isolation failed."); }
    }
    public void Dispose()
    {
        if (initialized) { DeleteProcThreadAttributeList(attributes); initialized = false; }
        foreach (var memory in owned) { Marshal.FreeHGlobal(memory); }
        owned.Clear(); attributes = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Explicit, Size = 112)]
    private struct Startup
    {
        [FieldOffset(0)] internal uint Size;
        [FieldOffset(60)] internal uint Flags;
        [FieldOffset(80)] internal IntPtr Input;
        [FieldOffset(88)] internal IntPtr Output;
        [FieldOffset(96)] internal IntPtr Error;
        [FieldOffset(104)] internal IntPtr Attributes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ChildInformation { internal IntPtr ProcessHandle; internal IntPtr ThreadHandle; internal uint ProcessId; internal uint ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, uint count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint key, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, [In, Out] char[] command, IntPtr processSecurity, IntPtr threadSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out ChildInformation child);
}
