namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerWebApplicationBuilder
{
    public static WebApplicationBuilder Create(string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        return WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
        });
    }
}
