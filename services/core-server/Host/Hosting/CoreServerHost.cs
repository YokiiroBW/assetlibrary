namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerHost
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length > 0 && arguments[0] == "--read-only-worker")
        {
            return await Trial.ReadOnlyWorkerCommand.RunAsync(arguments).ConfigureAwait(false);
        }

        if (arguments.Length == 2 && arguments[0] == "--read-only-trial")
        {
            return await Trial.TrialHost.RunAsync(arguments[1]).ConfigureAwait(false);
        }

        if (arguments.Length == 3 && arguments[0] == "--trial-operator")
        {
            return await Trial.TrialHost.RunAsync(arguments[1], arguments[2]).ConfigureAwait(false);
        }

        if (arguments.Length == 2 && arguments[0] == "--trial-health-probe")
        {
            return await Trial.TrialHealthProbe.RunAsync(arguments[1]).ConfigureAwait(false);
        }

        var parsed = CoreServerHostOptions.Parse(arguments, Environment.GetEnvironmentVariable);
        if (!parsed.IsValid)
        {
            await Console.Error.WriteLineAsync(CoreServerHostJson.Error(parsed.ErrorCode)).ConfigureAwait(false);
            return (int)CoreServerExitCode.InvalidConfiguration;
        }

        return await CoreServerCommandDispatcher.ExecuteAsync(parsed.Options!).ConfigureAwait(false);
    }
}
