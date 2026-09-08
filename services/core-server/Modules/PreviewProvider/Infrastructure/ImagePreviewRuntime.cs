using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Application;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.Modules.PreviewProvider.Infrastructure;

public static class ImagePreviewRuntime
{
    public static IImagePreviewQuery Create(ILibraryScanTargetQuery roots, ReadOnlyWorkerProcessOptions sourceWorkers,
        string? workerExecutable, string profileDirectory)
    {
        if (string.IsNullOrWhiteSpace(workerExecutable)) return new UnavailableImagePreviewQuery();
        if (!Path.IsPathFullyQualified(workerExecutable) || !File.Exists(workerExecutable)
            || (File.GetAttributes(workerExecutable) & FileAttributes.ReparsePoint) != 0)
        {
            return new UnavailableImagePreviewQuery();
        }
        return new ImagePreviewService(roots, new ProcessImageSourceReader(sourceWorkers),
            new IsolatedImageDecoder(workerExecutable, profileDirectory));
    }

    private sealed class UnavailableImagePreviewQuery : IImagePreviewQuery
    {
        public ValueTask<IImagePreviewLease> PrepareAsync(ImagePreviewSource source, ImagePreviewVariant variant,
            CancellationToken cancellationToken) => throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
    }
}
