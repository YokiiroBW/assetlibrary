using System.Text.Json;

namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerHostLogging
{
    public static void Configure(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(console => console.JsonWriterOptions = new JsonWriterOptions
        {
            Indented = false,
        });
        builder.Logging.AddFilter("Microsoft", LogLevel.None);
    }

    public static void RegisterLifecycle(WebApplication application, int port)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Lifetime.ApplicationStarted.Register(() => CoreServerHostLog.Started(
            application.Logger,
            CoreServerBuildInfo.CurrentContract,
            port,
            productionFileWrites: false));
        application.Lifetime.ApplicationStopping.Register(() => CoreServerHostLog.Stopping(
            application.Logger,
            CoreServerBuildInfo.CurrentContract));
    }
}
