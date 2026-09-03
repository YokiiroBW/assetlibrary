namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerHost
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        var parsed = CoreServerHostOptions.Parse(arguments, Environment.GetEnvironmentVariable);
        if (!parsed.IsValid)
        {
            await Console.Error.WriteLineAsync(CoreServerHostJson.Error(parsed.ErrorCode)).ConfigureAwait(false);
            return (int)CoreServerExitCode.InvalidConfiguration;
        }

        return await CoreServerCommandDispatcher.ExecuteAsync(parsed.Options!).ConfigureAwait(false);
    }
}
