using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal static class WicThumbnailDecoder
{
    internal static ThumbnailPixels Decode(byte[] png)
    {
        var expected = PngThumbnailContainer.Validate(png);
        var initialized = CoInitializeEx(IntPtr.Zero, 0);
        Marshal.ThrowExceptionForHR(initialized);
        try { return DecodeInitialized(png, expected); }
        finally { CoUninitialize(); }
    }

    private static ThumbnailPixels DecodeInitialized(byte[] png, (uint Width, uint Height) expected)
    {
        using var input = new WicObject(SHCreateMemStream(png, (uint)png.Length));
        using var decoder = WicObject.Create(new Guid("389ea17b-5078-4cde-b6ef-25c15175c751"), new Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf"));
        Marshal.ThrowExceptionForHR(decoder.Method<InitializeDecoder>(4)(decoder.Handle, input.Handle, 0));
        Marshal.ThrowExceptionForHR(decoder.Method<GetCount>(12)(decoder.Handle, out var frames));
        if (frames != 1) { throw new InvalidDataException("PNG must contain one frame."); }
        Marshal.ThrowExceptionForHR(decoder.Method<GetFrame>(13)(decoder.Handle, 0, out var framePointer));
        using var frame = new WicObject(framePointer);
        Marshal.ThrowExceptionForHR(frame.Method<GetSize>(3)(frame.Handle, out var width, out var height));
        if ((width, height) != expected) { throw new InvalidDataException("PNG decoder dimensions mismatch."); }
        using var converter = WicObject.Create(new Guid("1a3f11dc-b514-4b17-8c5f-2154513852f1"), new Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"));
        var format = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc910");
        Marshal.ThrowExceptionForHR(converter.Method<InitializeConverter>(8)(converter.Handle, frame.Handle, in format, 0, IntPtr.Zero, 0, 0));
        var pixels = new byte[checked((int)(width * height * 4))];
        Marshal.ThrowExceptionForHR(converter.Method<CopyPixels>(7)(converter.Handle, IntPtr.Zero, width * 4, (uint)pixels.Length, pixels));
        return new ThumbnailPixels(width, height, pixels);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitializeDecoder(IntPtr self, IntPtr stream, uint cache);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCount(IntPtr self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFrame(IntPtr self, uint index, out IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetSize(IntPtr self, out uint width, out uint height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitializeConverter(IntPtr self, IntPtr source, in Guid format, uint dither, IntPtr palette, double threshold, uint translation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CopyPixels(IntPtr self, IntPtr rectangle, uint stride, uint length, [Out] byte[] bytes);

    [DllImport("shlwapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SHCreateMemStream(byte[] bytes, uint size);
    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoInitializeEx(IntPtr reserved, uint concurrency);
    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CoUninitialize();
}

[SupportedOSPlatform("windows")]
internal sealed class WicObject(IntPtr pointer) : IDisposable
{
    internal IntPtr Handle { get; private set; } = pointer != IntPtr.Zero ? pointer : throw new InvalidDataException("WIC object unavailable.");
    internal T Method<T>(int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(Handle), slot * IntPtr.Size));
    internal static WicObject Create(Guid classId, Guid interfaceId)
    {
        var factoryId = new Guid("00000001-0000-0000-c000-000000000046");
        // Pin the inbox implementation itself; do not discover arbitrary registered image codecs.
        Marshal.ThrowExceptionForHR(DllGetClassObject(in classId, in factoryId, out var factoryPointer));
        using var factory = new WicObject(factoryPointer);
        Marshal.ThrowExceptionForHR(factory.Method<CreateInstance>(3)(factory.Handle, IntPtr.Zero, in interfaceId, out var instance));
        return new WicObject(instance);
    }
    public void Dispose() { if (Handle != IntPtr.Zero) { _ = Marshal.Release(Handle); Handle = IntPtr.Zero; } }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateInstance(IntPtr self, IntPtr outer, in Guid interfaceId, out IntPtr instance);
    [DllImport("windowscodecs.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DllGetClassObject(in Guid classId, in Guid interfaceId, out IntPtr factory);
}
