using System.Reflection;

namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerCommandDispatcher
{
    public static async Task<int> ExecuteAsync(CoreServerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        switch (options.Command)
        {
            case CoreServerHostCommand.BuildInfo:
                await Console.Out.WriteLineAsync(CoreServerHostJson.Serialize(
                    CoreServerBuildInfo.Read(Assembly.GetExecutingAssembly()))).ConfigureAwait(false);
                return (int)CoreServerExitCode.Success;
            case CoreServerHostCommand.HealthProbe:
                var healthy = await CoreServerHealthProbe.RunAsync(
                    options,
                    handler: null,
                    CancellationToken.None).ConfigureAwait(false);
                return healthy ? (int)CoreServerExitCode.Success : (int)CoreServerExitCode.Unavailable;
            case CoreServerHostCommand.Run:
                return await CoreServerApplication.RunAsync(options).ConfigureAwait(false);
            default:
                await Console.Error.WriteLineAsync(CoreServerHostJson.Error("invalid_command")).ConfigureAwait(false);
                return (int)CoreServerExitCode.InvalidConfiguration;
        }
    }
}
