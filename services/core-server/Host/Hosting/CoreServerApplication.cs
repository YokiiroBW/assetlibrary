namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerApplication
{
    public static async Task<int> RunAsync(CoreServerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!await CoreServerStateBoundary.IsWritableAsync(options.StatePath).ConfigureAwait(false))
        {
            await Console.Error.WriteLineAsync(CoreServerHostJson.Error("state_path_unwritable"))
                .ConfigureAwait(false);
            return (int)CoreServerExitCode.InvalidConfiguration;
        }

        try
        {
            await using var application = CoreServerApplicationFactory.Build(options);
            await application.RunAsync().ConfigureAwait(false);
            return (int)CoreServerExitCode.Success;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or NotSupportedException)
        {
            await Console.Error.WriteLineAsync(CoreServerHostJson.Error("host_start_failed")).ConfigureAwait(false);
            return (int)CoreServerExitCode.SoftwareError;
        }
    }
}
