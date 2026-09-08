using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

[SupportedOSPlatform("windows")]
internal sealed class WindowsImageStartup : IDisposable
{
    private readonly List<nint> allocations = [];
    private nint attributes;
    private bool initialized;
    private WindowsImageCapability? compatibility;

    public WindowsImageStartup(nint sid, nint job, nint[] pipes)
    {
        try
        {
            nuint size = 0;
            _ = Native.InitializeProcThreadAttributeList(nint.Zero, 4, 0, ref size);
            var error = Marshal.GetLastPInvokeError();
            if (size is 0 or > 65536 || error != 122) throw new ReadOnlyWorkerException("preview_startup_attributes_failed", error);
            attributes = Marshal.AllocHGlobal(checked((int)size));
            if (!Native.InitializeProcThreadAttributeList(attributes, 4, 0, ref size))
            {
                throw new ReadOnlyWorkerException("preview_startup_attributes_failed", Marshal.GetLastPInvokeError());
            }
            initialized = true;

            compatibility = WindowsImageCapability.Create();
            var capabilityEntry = Allocate(Marshal.SizeOf<SidAttributes>());
            Marshal.StructureToPtr(new SidAttributes { Sid = compatibility.Sid, Attributes = 4 }, capabilityEntry, fDeleteOld: false);
            var capabilities = Allocate(Marshal.SizeOf<SecurityCapabilities>());
            Marshal.StructureToPtr(new SecurityCapabilities { AppContainerSid = sid, Capabilities = capabilityEntry, Count = 1 }, capabilities, fDeleteOld: false);
            Add(0x20009, capabilities, checked((nuint)Marshal.SizeOf<SecurityCapabilities>()));
            var handles = Allocate(pipes.Length * nint.Size);
            Marshal.Copy(pipes, 0, handles, pipes.Length);
            Add(0x20002, handles, checked((nuint)(pipes.Length * nint.Size)));
            var jobs = Allocate(nint.Size);
            Marshal.WriteIntPtr(jobs, job);
            Add(0x2000d, jobs, checked((nuint)nint.Size));
            var policy = Allocate(sizeof(uint));
            Marshal.WriteInt32(policy, 1);
            Add(0x2000f, policy, sizeof(uint)); // LPAC: opt out of ALL_APPLICATION_PACKAGES access.
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public NativeProcessInformation Start(WindowsImageProfile profile, nint[] pipes, CancellationToken token = default)
    {
        var startup = new StartupExtended
        {
            Startup = new StartupInformation
            {
                Size = checked((uint)Marshal.SizeOf<StartupExtended>()),
                Flags = 0x100,
                Input = pipes[0],
                Output = pipes[1],
                Error = pipes[2],
            },
            Attributes = attributes,
        };
        var environment = Marshal.StringToHGlobalUni(EnvironmentBlock(profile.DirectoryPath));
        try
        {
            token.ThrowIfCancellationRequested();
            profile.BeginLaunch();
            token.ThrowIfCancellationRequested();
            const uint flags = 0x00000004 | 0x00000400 | 0x00080000 | 0x08000000;
            if (!Native.CreateProcessW(profile.Executable, ('"' + profile.Executable + '"' + '\0').ToCharArray(),
                nint.Zero, nint.Zero, true, flags, environment, profile.DirectoryPath, ref startup, out var process))
            {
                throw new ReadOnlyWorkerException("preview_process_creation_failed", Marshal.GetLastPInvokeError());
            }
            return process;
        }
        finally
        {
            Marshal.FreeHGlobal(environment);
        }
    }

    private static string EnvironmentBlock(string temporaryDirectory)
    {
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_EnableDiagnostics"] = "0",
            ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ["WINDIR"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ["LOCALAPPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["TEMP"] = temporaryDirectory,
            ["TMP"] = temporaryDirectory,
        };
        return string.Join('\0', values.Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
    }

    private nint Allocate(int size)
    {
        var memory = Marshal.AllocHGlobal(size);
        allocations.Add(memory);
        return memory;
    }

    private void Add(nuint kind, nint value, nuint bytes)
    {
        if (!Native.UpdateProcThreadAttribute(attributes, 0, kind, value, bytes, nint.Zero, nint.Zero))
        {
            throw new ReadOnlyWorkerException("preview_startup_attributes_failed", Marshal.GetLastPInvokeError());
        }
    }

    public void Dispose()
    {
        if (attributes != nint.Zero)
        {
            if (initialized) Native.DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes);
            attributes = nint.Zero;
        }
        foreach (var allocation in allocations) Marshal.FreeHGlobal(allocation);
        allocations.Clear();
        compatibility?.Dispose();
        compatibility = null;
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        public nint AppContainerSid;
        public nint Capabilities;
        public uint Count;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAttributes
    {
        public nint Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInformation
    {
        public uint Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCharacters;
        public uint YCharacters;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort ReservedSize;
        public nint ReservedBytes;
        public nint Input;
        public nint Output;
        public nint Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupExtended
    {
        public StartupInformation Startup;
        public nint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint bytes);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value,
            nuint size, nint previous, nint returnedSize);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void DeleteProcThreadAttributeList(nint list);

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(string application, [In, Out] char[] commandLine, nint processAttributes,
            nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment,
            string directory, ref StartupExtended startup, out NativeProcessInformation information);
    }
}
