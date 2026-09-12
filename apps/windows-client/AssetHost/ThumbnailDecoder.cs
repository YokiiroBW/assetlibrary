using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal sealed class ThumbnailDecoder(string? executable = null)
{
    internal async Task<ThumbnailPixels> DecodeAsync(byte[] png, CancellationToken token)
    {
        var expected = PngThumbnailContainer.Validate(png);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using var job = new ThumbnailDecodeJob();
        await using var input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        await using var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        await using var errors = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var handles = new[] { input.ClientSafePipeHandle.DangerousGetHandle(), output.ClientSafePipeHandle.DangerousGetHandle(), errors.ClientSafePipeHandle.DangerousGetHandle() };
        using var startup = new ThumbnailDecodeStartup(job.Handle.DangerousGetHandle(), handles);
        var path = executable ?? Path.Combine(AppContext.BaseDirectory, "AssetLibrary.Host.exe");
        deadline.Token.ThrowIfCancellationRequested();
        using var process = startup.Start(Path.GetFullPath(path), handles);
        input.DisposeLocalCopyOfClientHandle(); output.DisposeLocalCopyOfClientHandle(); errors.DisposeLocalCopyOfClientHandle();
        using var cancellation = deadline.Token.Register(job.Terminate);
        var pixels = ThumbnailDecodeWire.ReadOutputAsync(output, deadline.Token);
        var diagnostic = ReadDiagnosticAsync(errors, deadline.Token);
        try
        {
            await ThumbnailDecodeWire.WriteInputAsync(input, png, deadline.Token).ConfigureAwait(false);
            await input.DisposeAsync().ConfigureAwait(false);
            var result = await pixels.ConfigureAwait(false);
            await diagnostic.ConfigureAwait(false);
            await WaitForExitAsync(process, deadline.Token).ConfigureAwait(false);
            if (!GetExitCodeProcess(process, out var code) || code != 0 || (result.Width, result.Height) != expected)
            { throw new InvalidDataException("Thumbnail helper result rejected."); }
            return result;
        }
        finally
        {
            job.Terminate();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await WaitForExitAsync(process, cleanup.Token).ConfigureAwait(false);
            try { await Task.WhenAll(pixels, diagnostic).ConfigureAwait(false); }
            catch (Exception failure) when (failure is IOException or InvalidDataException or OperationCanceledException)
            { /* The failed child is reaped; no asynchronous pipe buffer remains owned by it. */ }
        }
    }

    private static async Task ReadDiagnosticAsync(Stream errors, CancellationToken token)
    {
        var bytes = new byte[1025]; var total = 0;
        while (true)
        {
            var read = await errors.ReadAsync(bytes.AsMemory(total), token).ConfigureAwait(false);
            if (read == 0) { return; }
            total += read;
            if (total == bytes.Length) { throw new InvalidDataException("Thumbnail diagnostic limit exceeded."); }
        }
    }
    internal static async Task WaitForExitAsync(SafeProcessHandle process, CancellationToken token)
    {
        while (true)
        {
            var state = WaitForSingleObject(process, 0);
            if (state == 0) { return; }
            if (state != 258) { throw new IOException("Thumbnail child wait failed."); }
            await Task.Delay(10, token).ConfigureAwait(false);
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint timeout);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
}
