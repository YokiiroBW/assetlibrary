using System.Reflection;
using System.Text.Json;

if (args.Contains("--build-info", StringComparer.Ordinal))
    return BuildInfo();

if (args.Contains("--health-probe", StringComparer.Ordinal))
    return await HealthProbe();

var builder = WebApplication.CreateBuilder(args);
var dataPath = builder.Configuration["SPIKE_DATA_PATH"];
if (string.IsNullOrWhiteSpace(dataPath))
    return Fail("missing_configuration", "SPIKE_DATA_PATH is required");

var portText = builder.Configuration["SPIKE_PORT"] ?? "5080";
if (!int.TryParse(portText, out var port) || port is < 1024 or > 65535)
    return Fail("invalid_configuration", "SPIKE_PORT must be between 1024 and 65535");
var bindHost = builder.Configuration["SPIKE_BIND_HOST"] ?? "127.0.0.1";
if (string.IsNullOrWhiteSpace(bindHost) || bindHost.Any(char.IsWhiteSpace))
    return Fail("invalid_configuration", "SPIKE_BIND_HOST must be a host name or address");

try
{
    Directory.CreateDirectory(dataPath);
    var probe = Path.Combine(dataPath, ".write-probe");
    await File.WriteAllTextAsync(probe, "probe");
    File.Delete(probe);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    return Fail("data_path_unwritable", "SPIKE_DATA_PATH is not writable");
}

builder.Host.UseWindowsService();
builder.WebHost.UseUrls($"http://{bindHost}:{port}");
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
var app = builder.Build();
app.MapGet("/healthz", () => Results.Json(new { status = "ok", contract = "m0-004/v1" }));
app.MapGet("/readyz", () => Results.Json(new { status = "ready", contract = "m0-004/v1", data_path = "configured" }));
app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation("spike_started contract={Contract} port={Port}", "m0-004/v1", port));
app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation("spike_stopping contract={Contract}", "m0-004/v1"));
await app.RunAsync();
return 0;

static int Fail(string code, string message)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { level = "error", code, message }));
    Environment.Exit(78);
    return 0;
}

static int BuildInfo()
{
    var version = Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion;
    Console.WriteLine(JsonSerializer.Serialize(new { contract = "m0-004/v1", informational_version = version }));
    return string.IsNullOrWhiteSpace(version) ? 1 : 0;
}

static async Task<int> HealthProbe()
{
    var host = Environment.GetEnvironmentVariable("SPIKE_PROBE_HOST") ?? "127.0.0.1";
    var port = Environment.GetEnvironmentVariable("SPIKE_PORT") ?? "5080";
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    try
    {
        using var response = await client.GetAsync($"http://{host}:{port}/healthz");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException) { return 1; }
    catch (TaskCanceledException) { return 1; }
}
