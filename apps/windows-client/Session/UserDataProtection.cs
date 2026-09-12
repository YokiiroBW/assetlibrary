using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace AssetLibrary.Windows.Session;

[SupportedOSPlatform("windows")]
internal static class UserDataProtection
{
    internal static byte[] Protect(byte[] value) => Transform(value, protect: true);
    internal static byte[] Unprotect(byte[] value) => Transform(value, protect: false);

    private static byte[] Transform(byte[] value, bool protect)
    {
        var input = new DataBlob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        DataBlob output = default;
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            // No LOCAL_MACHINE flag: the OS binds the encrypted blob to the current user profile.
            var success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) { throw new CryptographicException("Protected login is unavailable."); }
            if (output.Length is < 1 or > 32768) { throw new CryptographicException("Invalid protected login size."); }
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Zero(input);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Zero(output); _ = LocalFree(output.Data); }
        }
    }

    private static void Zero(DataBlob blob)
    {
        for (var index = 0; index < blob.Length; ++index) { Marshal.WriteByte(blob.Data, index, 0); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { internal int Length; internal IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
