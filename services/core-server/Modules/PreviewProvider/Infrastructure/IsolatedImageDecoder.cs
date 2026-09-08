using System.Diagnostics;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.Modules.PreviewProvider.Application;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.Modules.PreviewProvider.Infrastructure;

internal sealed class IsolatedImageDecoder(string executable, string profileDirectory) : IImageDecoder
{
    public async ValueTask<byte[]> DecodeAsync(IImageSourceLease source, ImagePreviewVariant variant, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await using var child = await StartBoundedAsync(deadline.Token).ConfigureAwait(false);
            var diagnostics = ImageWorkerTransport.DrainErrorsAsync(child.Error, deadline.Token);
            var ready = await ImageWorkerTransport.ReadHeaderAsync(child.Output, deadline.Token).ConfigureAwait(false);
            ImageWorkerTransport.RequireStatus(ready, ImageWorkerStatus.Ready);
            if (ready.Length != 0 || ready.Width != 0 || ready.Height != 0) throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
            await child.Input.WriteAsync(ImageWorkerProtocol.Header((int)ImageWorkerStatus.Request, (int)variant,
                checked((int)source.Length)), deadline.Token).ConfigureAwait(false);
            await source.CopyToAsync(child.Input, deadline.Token).ConfigureAwait(false);
            await child.Input.FlushAsync(deadline.Token).ConfigureAwait(false);
            child.Input.Dispose();
            var result = await ImageWorkerTransport.ReadHeaderAsync(child.Output, deadline.Token).ConfigureAwait(false);
            ImageWorkerTransport.RequireStatus(result, ImageWorkerStatus.Success);
            if (result.Length is < 33 || result.Length > ImageWorkerProtocol.MaximumOutput((int)variant))
            {
                throw new ImagePreviewException(ImagePreviewFailure.LimitExceeded);
            }
            var png = new byte[result.Length];
            await child.Output.ReadExactlyAsync(png, deadline.Token).ConfigureAwait(false);
            ImageWorkerTransport.ValidatePng(png, result, variant);
            if (await child.Output.ReadAsync(new byte[1], deadline.Token).ConfigureAwait(false) != 0)
            {
                throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
            }
            await child.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await diagnostics.ConfigureAwait(false);
            if (child.ExitCode != 0) throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
            return png;
        }
        catch (ImageChildCleanupPendingException pending)
        {
            throw new ImageDecoderCleanupPendingException(pending.Completion,
                deadline.IsCancellationRequested ? ImagePreviewFailure.Timeout : ImagePreviewFailure.Unavailable);
        }
        catch (ImageDecoderCleanupPendingException)
        {
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception) when (deadline.IsCancellationRequested)
        {
            throw new ImagePreviewException(ImagePreviewFailure.Timeout);
        }
    }

    private async ValueTask<IImageChildProcess> StartBoundedAsync(CancellationToken token)
    {
        var startup = Task.Run(() => Start(token), CancellationToken.None);
        try { return await startup.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            throw new ImageDecoderCleanupPendingException(ReapLateStartupAsync(startup));
        }
    }

    private static async Task ReapLateStartupAsync(Task<IImageChildProcess> startup)
    {
        IImageChildProcess child;
        try { child = await startup.ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        try { await child.DisposeAsync().ConfigureAwait(false); }
        catch (ImageChildCleanupPendingException pending) { await pending.Completion.ConfigureAwait(false); }
    }

    private IImageChildProcess Start(CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) return WindowsImageProcess.Start(executable, profileDirectory, token);
        if (!OperatingSystem.IsLinux()) throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        start.Environment.Clear();
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        start.Environment["LANG"] = "C";
        return ImageChildProcess.Start(start, token);
    }
}
