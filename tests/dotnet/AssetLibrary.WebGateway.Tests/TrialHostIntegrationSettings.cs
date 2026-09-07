using System.Text.Json;

namespace AssetLibrary.WebGateway.Tests;

internal sealed record TrialHostIntegrationSettings
{
    public static JsonSerializerOptions Serialization { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Database { get; init; }
    public required Dictionary<string, string> Logins { get; init; }
    public required string RuntimeRoot { get; init; }
    public required string WebRoot { get; init; }
    public required string Node { get; init; }
    public required string PlaywrightModule { get; init; }
    public required string BrowserScript { get; init; }
    public required string Evidence { get; init; }
    public required string Dotnet { get; init; }
    public required string HostDll { get; init; }

    public static TrialHostIntegrationSettings Load()
    {
        var path = Environment.GetEnvironmentVariable("ASSETLIBRARY_TRIAL_E2E_SETTINGS");
        if (path is null)
        {
            if (Environment.GetEnvironmentVariable("ASSETLIBRARY_TRIAL_E2E_REQUIRED") == "1")
            {
                Assert.Fail("Required real trial settings are missing.");
            }

            Assert.Inconclusive("Real trial E2E was not enabled; this is not execution evidence.");
        }

        var settings = JsonSerializer.Deserialize<TrialHostIntegrationSettings>(File.ReadAllText(path!), Serialization)
            ?? throw new InvalidDataException("The real trial settings are invalid.");
        var runtime = Path.GetFullPath(settings.RuntimeRoot);
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        Assert.IsTrue(runtime.StartsWith(temporary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && Path.GetFileName(runtime).StartsWith("al20-", StringComparison.Ordinal), "E2E may only mutate its own temporary root.");
        Assert.AreEqual("127.0.0.1", settings.Host);
        Assert.IsTrue(settings.Database.StartsWith("v01003_trial_e2e_", StringComparison.Ordinal));
        Assert.IsTrue(settings.Logins.Values.All(value => value.StartsWith("v020_", StringComparison.Ordinal)));
        return settings;
    }

    public string Connection(string module) =>
        $"Host={Host};Port={Port};Database={Database};Username={Logins[module]};Timeout=5;Command Timeout=5";

    public override string ToString() => "[real trial settings]";
}
