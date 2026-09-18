using System.Text.Json;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// Runs the exact-duplicate workbench in a real Chromium against the real host. It is the browser half of
/// the trial: the page is the built Web artifact served by the trial host, every request it makes goes to
/// that host over HTTPS, and no route handler is installed anywhere. What it returns is what the page
/// itself observed.
/// </summary>
internal static class DedupTrialBrowser
{
    /// <summary>
    /// Drives the workbench for two libraries and returns what the page did. The second library is a
    /// different registration, so the page mints a new operation key for it and its start is a genuinely
    /// new durable task — which is what makes the cancellation in the script a cancellation of work in
    /// progress rather than a request the server has nothing left to stop.
    /// </summary>
    public static async Task<DedupTrialBrowserResult> RunAsync(
        TrialHostIntegrationFixture host,
        TrialHostIntegrationSettings settings,
        Guid libraryId,
        Guid secondLibraryId,
        string libraryRoot,
        string secondLibraryRoot)
    {
        using var result = await TrialBrowserProcess.RunAsync(
            host,
            settings.DedupBrowserScript,
            settings.Node,
            TimeSpan.FromSeconds(300),
            new
            {
                origin = host.Configuration.PublicOrigin,
                account_name = "trial-admin",
                password = TrialHostIntegrationAuthentication.Password,
                library_root = libraryRoot,
                second_library_root = secondLibraryRoot,
                library_id = libraryId,
                second_library_id = secondLibraryId,
                playwright_module = settings.PlaywrightModule,
                evidence = settings.Evidence,
                spki = TrialBrowserProcess.Spki(host),
            });
        var operations = result.RootElement.GetProperty("operations")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        return new DedupTrialBrowserResult(operations);
    }
}

/// <summary>What the browser phase observed: the workbench operations the page really called.</summary>
internal sealed record DedupTrialBrowserResult(IReadOnlyList<string> Operations);
