using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationBrowser
{
    public static async Task<Guid> RunAsync(TrialHostIntegrationFixture host, TrialHostIntegrationSettings settings,
        TrialHostIntegrationAssets assets, string phase, Guid? libraryId = null)
    {
        using var key = host.Certificate.GetRSAPublicKey()!;
        var spki = Convert.ToBase64String(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
        var start = new ProcessStartInfo(settings.Node)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(settings.BrowserScript);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Browser test did not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new
            {
                phase,
                origin = host.Configuration.PublicOrigin,
                account_name = "trial-admin",
                password = TrialHostIntegrationAuthentication.Password,
                library_root = assets.LibraryRoot,
                library_id = libraryId,
                playwright_module = settings.PlaywrightModule,
                evidence = settings.Evidence,
                spki,
            }));
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            Assert.AreEqual(0, process.ExitCode, await error);
            using var result = JsonDocument.Parse(await output);
            return result.RootElement.GetProperty("library_id").GetGuid();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
