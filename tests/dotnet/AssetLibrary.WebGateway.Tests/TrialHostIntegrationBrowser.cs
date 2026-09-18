using System.Text.Json;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationBrowser
{
    public static async Task<Guid> RunAsync(TrialHostIntegrationFixture host, TrialHostIntegrationSettings settings,
        TrialHostIntegrationAssets assets, string phase, Guid? libraryId = null)
    {
        using var result = await TrialBrowserProcess.RunAsync(
            host,
            settings.BrowserScript,
            settings.Node,
            TimeSpan.FromSeconds(120),
            new
            {
                phase,
                origin = host.Configuration.PublicOrigin,
                account_name = "trial-admin",
                password = TrialHostIntegrationAuthentication.Password,
                library_root = assets.LibraryRoot,
                library_id = libraryId,
                playwright_module = settings.PlaywrightModule,
                evidence = settings.Evidence,
                spki = TrialBrowserProcess.Spki(host),
            });
        return result.RootElement.GetProperty("library_id").GetGuid();
    }
}
