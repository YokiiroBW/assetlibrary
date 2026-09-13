using System.IO.Pipes;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal sealed class ThumbnailDecoder(string? executable = null)
{
    internal Task<ThumbnailPixels> DecodeAsync(byte[] png, CancellationToken token) => DecodeAsync(png, DerivedImageProfile.Thumbnail512, token);
    internal Task<ThumbnailPixels> DecodePreviewAsync(byte[] png, CancellationToken token) => DecodeAsync(png, DerivedImageProfile.Preview1600, token);
    private async Task<ThumbnailPixels> DecodeAsync(byte[] png, DerivedImageProfile profile, CancellationToken token)
    {
        var expected = PngThumbnailContainer.Validate(png, profile);
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
        using var process = startup.Start(Path.GetFullPath(path), handles, profile);
        input.DisposeLocalCopyOfClientHandle(); output.DisposeLocalCopyOfClientHandle(); errors.DisposeLocalCopyOfClientHandle();
        using var cancellation = deadline.Token.Register(job.Terminate);
        var pixels = ThumbnailDecodeWire.ReadOutputAsync(output, deadline.Token, profile);
        var diagnostic = ReadDiagnosticAsync(errors, deadline.Token);
        try
        {
            await ThumbnailDecodeWire.WriteInputAsync(input, png, deadline.Token).ConfigureAwait(false);
            await input.DisposeAsync().ConfigureAwait(false);
            var result = await pixels.ConfigureAwait(false);
            await diagnostic.ConfigureAwait(false);
            await WaitForExitAsync(process, deadline.Token).ConfigureAwait(false);
            if (!ThumbnailDecodeJob.SuccessfulExit(process) || (result.Width, result.Height) != expected)
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
    internal static Task WaitForExitAsync(SafeProcessHandle process, CancellationToken token) => ThumbnailDecodeJob.WaitForExitAsync(process, token);
}
