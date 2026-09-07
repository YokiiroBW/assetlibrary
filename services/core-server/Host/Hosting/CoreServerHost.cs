namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerHost
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length > 0 && arguments[0] == "--read-only-worker")
        {
            return await Trial.ReadOnlyWorkerCommand.RunAsync(arguments).ConfigureAwait(false);
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
