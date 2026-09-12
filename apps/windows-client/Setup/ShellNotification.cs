using System.Runtime.InteropServices;

namespace AssetLibrary.Windows.Setup;

internal static class ShellNotification
{
    internal static bool Notify()
    {
        bool notified = false;
        Thread thread = new(() => notified = NotifyOnApartment()) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(3)) && notified;
    }

    private static bool NotifyOnApartment()
    {
        nint desktop = 0;
        bool initialized = false;
        try
        {
            if (CoInitializeEx(0, 2) < 0) { return false; }
            initialized = true;
            if (SHGetSpecialFolderLocation(0, 0, out desktop) < 0 || desktop == 0) { return false; }
            SHChangeNotify(0x08000000, 0, 0, 0);
            SHChangeNotify(0x00001000, 0x2000, desktop, 0);
            return true;
        }
        finally
        {
            if (desktop != 0) { Marshal.FreeCoTaskMem(desktop); }
            if (initialized) { CoUninitialize(); }
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CoUninitialize();
    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHGetSpecialFolderLocation(nint owner, int folder, out nint pidl);
    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void SHChangeNotify(uint id, uint flags, nint first, nint second);
}
