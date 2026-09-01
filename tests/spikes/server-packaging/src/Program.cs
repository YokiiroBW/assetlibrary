using System.Text.Json;

Dictionary<string, string> options;
try { options = ReadOptions(args); }
catch (ArgumentException ex) { return Fail("invalid_configuration", ex.Message); }
if (args.Contains("--health-probe", StringComparer.Ordinal))
    return await HealthProbe(options);

var builder = WebApplication.CreateBuilder(args);
var dataPath = OptionOrEnvironment(options, "--spike-data-path", "SPIKE_DATA_PATH");
if (string.IsNullOrWhiteSpace(dataPath))
    return Fail("missing_configuration", "SPIKE_DATA_PATH is required");
var portText = OptionOrEnvironment(options, "--spike-port", "SPIKE_PORT") ?? "5080";
if (!int.TryParse(portText, out var port) || port is < 1024 or > 65535)
    return Fail("invalid_configuration", "SPIKE_PORT must be between 1024 and 65535");
var bindHost = OptionOrEnvironment(options, "--spike-bind-host", "SPIKE_BIND_HOST") ?? "127.0.0.1";
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

static Dictionary<string, string> ReadOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is not ("--spike-data-path" or "--spike-port" or "--spike-bind-host" or "--spike-probe-host"))
            continue;
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"missing value for {args[i]}");
        if (!options.TryAdd(args[i], args[++i]))
            throw new ArgumentException($"duplicate option {args[i]}");
    }
    return options;
}

static string? OptionOrEnvironment(IReadOnlyDictionary<string, string> options, string option, string variable)
    => options.TryGetValue(option, out var value) ? value : Environment.GetEnvironmentVariable(variable);

static int Fail(string code, string message)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { level = "error", code, message }));
    return 78;
}

static async Task<int> HealthProbe(IReadOnlyDictionary<string, string> options)
{
    var host = OptionOrEnvironment(options, "--spike-probe-host", "SPIKE_PROBE_HOST") ?? "127.0.0.1";
    var port = OptionOrEnvironment(options, "--spike-port", "SPIKE_PORT") ?? "5080";
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    try
    {
        if (!int.TryParse(port, out var parsedPort) || parsedPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(host) || host.Any(char.IsWhiteSpace))
            return 1;
        using var response = await client.GetAsync($"http://{host}:{parsedPort}/healthz");
        if (!response.IsSuccessStatusCode)
            return 1;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        return root.TryGetProperty("status", out var status) && status.GetString() == "ok" &&
               root.TryGetProperty("contract", out var contract) && contract.GetString() == "m0-004/v1" ? 0 : 1;
    }
    catch (HttpRequestException) { return 1; }
    catch (TaskCanceledException) { return 1; }
    catch (JsonException) { return 1; }
}
