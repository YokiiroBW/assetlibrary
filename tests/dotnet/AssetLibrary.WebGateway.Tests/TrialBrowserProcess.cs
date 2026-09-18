using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// Starts one browser script against the trial host and hands back what it printed. Both real trials drive
/// a real Chromium this way — the browse trial through one script and the dedup trial through another — so
/// the certificate pinning, the deadline and the failure reporting live here rather than in each script's
/// caller. A script that fails reports on stderr, which is what the assertion carries.
/// </summary>
internal static class TrialBrowserProcess
{
    private static readonly JsonSerializerOptions Serialization = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static async Task<JsonDocument> RunAsync(
        TrialHostIntegrationFixture host,
        string script,
        string node,
        TimeSpan deadline,
        object settings)
    {
        var start = new ProcessStartInfo(node)
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
        start.ArgumentList.Add(script);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Browser test did not start.");
        using var limit = new CancellationTokenSource(deadline);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(settings, Serialization));
            process.StandardInput.Close();
            await process.WaitForExitAsync(limit.Token);
            Assert.AreEqual(0, process.ExitCode, await error);
            return JsonDocument.Parse(await output);
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

    /// <summary>
    /// The fingerprint the browser is told to accept, which is this host's own certificate's public key.
    /// Chromium then accepts exactly this host and nothing else, so the page really talks to the trial over
    /// HTTPS rather than to something that merely has a trusted name.
    /// </summary>
    public static string Spki(TrialHostIntegrationFixture host)
    {
        using var key = host.Certificate.GetRSAPublicKey()!;
        return Convert.ToBase64String(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
    }
}
