using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Application;
using AssetLibrary.Modules.PreviewProvider.Contracts;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.PreviewProvider.Infrastructure;

public static class ImagePreviewRuntime
{
    public static IImagePreviewQuery Create(ILibraryScanTargetQuery roots, ReadOnlyWorkerProcessOptions sourceWorkers,
        string? workerExecutable, string profileDirectory, ILoggerFactory logs, string? socketPath = null)
    {
        if (!string.IsNullOrEmpty(socketPath))
        {
            if (!string.IsNullOrEmpty(workerExecutable) || !OperatingSystem.IsLinux()
                || !string.Equals(socketPath, UnixSocketImageDecoder.SocketPath, StringComparison.Ordinal))
                return new UnavailableImagePreviewQuery();
            return new ImagePreviewService(roots, new ProcessImageSourceReader(sourceWorkers),
                new UnixSocketImageDecoder(), logs.CreateLogger<ImagePreviewService>());
        }
        if (string.IsNullOrWhiteSpace(workerExecutable)) return new UnavailableImagePreviewQuery();
        if (!Path.IsPathFullyQualified(workerExecutable) || !File.Exists(workerExecutable)
            || (File.GetAttributes(workerExecutable) & FileAttributes.ReparsePoint) != 0)
        {
            return new UnavailableImagePreviewQuery();
        }
        return new ImagePreviewService(roots, new ProcessImageSourceReader(sourceWorkers),
            new IsolatedImageDecoder(workerExecutable, profileDirectory), logs.CreateLogger<ImagePreviewService>());
    }

    private sealed class UnavailableImagePreviewQuery : IImagePreviewQuery
    {
        public ValueTask<IImagePreviewLease> PrepareAsync(ImagePreviewSource source, ImagePreviewVariant variant,
            CancellationToken cancellationToken) => throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
    }
}
