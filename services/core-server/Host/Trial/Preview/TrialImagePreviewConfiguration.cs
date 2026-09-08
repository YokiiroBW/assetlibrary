using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Contracts;
using AssetLibrary.Modules.PreviewProvider.Infrastructure;

namespace AssetLibrary.CoreServer.Hosting.Trial.Preview;

internal static class TrialImagePreviewConfiguration
{
    public static void Configure(IServiceCollection services, TrialConfiguration configuration,
        ILibraryScanTargetQuery roots, ReadOnlyWorkerProcessOptions workers)
    {
        services.AddSingleton<IImagePreviewQuery>(provider => ImagePreviewRuntime.Create(roots, workers,
            Environment.GetEnvironmentVariable("ASSETLIBRARY_IMAGE_PREVIEW_WORKER"),
            Path.Combine(configuration.StatePath, "image-preview-profiles"), provider.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<AuthorizedImagePreviewService>();
    }
}
