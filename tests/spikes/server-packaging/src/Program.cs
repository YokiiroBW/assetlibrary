using System.Reflection;
using System.Text.Json;

if (args.Contains("--build-info", StringComparer.Ordinal))
    return BuildInfo();

Dictionary<string, string> options;
try { options = ReadOptions(args); }
catch (ArgumentException ex) { return Fail("invalid_configuration", ex.Message); }

if (args.Contains("--health-probe", StringComparer.Ordinal))
    return await HealthProbe(options);

var builder = WebApplication.CreateBuilder(args);
var dataPath = OptionOrConfiguration(options, "SPIKE_DATA_PATH", builder.Configuration);
if (string.IsNullOrWhiteSpace(dataPath))
    return Fail("missing_configuration", "SPIKE_DATA_PATH is required");

var portText = OptionOrConfiguration(options, "SPIKE_PORT", builder.Configuration) ?? "5080";
if (!int.TryParse(portText, out var port) || port is < 1024 or > 65535)
    return Fail("invalid_configuration", "SPIKE_PORT must be between 1024 and 65535");
var bindHost = OptionOrConfiguration(options, "SPIKE_BIND_HOST", builder.Configuration) ?? "127.0.0.1";
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
    return 78;
}

static Dictionary<string, string> ReadOptions(string[] arguments)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < arguments.Length; index++)
    {
        var argument = arguments[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal)) continue;
        // The original Windows evidence and NAS guardrail branch used different
        // spellings of the same test-local settings. Normalize before validation.
        var key = argument[2..].Replace('-', '_').ToUpperInvariant();
        if (key is not ("SPIKE_DATA_PATH" or "SPIKE_PORT" or "SPIKE_BIND_HOST" or "SPIKE_PROBE_HOST")) continue;
        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"missing value for {key}");
        if (!values.TryAdd(key, arguments[++index]))
            throw new ArgumentException($"duplicate option {key}");
    }
    return values;
}

static string? OptionOrConfiguration(IReadOnlyDictionary<string, string> options, string key, IConfiguration configuration)
    => options.TryGetValue(key, out var value) ? value : configuration[key];

static int BuildInfo()
{
    var version = Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion;
    Console.WriteLine(JsonSerializer.Serialize(new { contract = "m0-004/v1", informational_version = version }));
    return string.IsNullOrWhiteSpace(version) ? 1 : 0;
}

static async Task<int> HealthProbe(IReadOnlyDictionary<string, string> options)
{
    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
    var host = OptionOrConfiguration(options, "SPIKE_PROBE_HOST", configuration) ?? "127.0.0.1";
    var port = OptionOrConfiguration(options, "SPIKE_PORT", configuration) ?? "5080";
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    try
    {
        if (!int.TryParse(port, out var parsedPort) || parsedPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(host) || host.Any(char.IsWhiteSpace))
            return 1;
        using var response = await client.GetAsync($"http://{host}:{parsedPort}/healthz", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) return 1;
        using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        var buffer = new byte[4097];
        var total = 0;
        while (total < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(total), deadline.Token);
            if (count == 0) break;
            total += count;
        }
        if (total == buffer.Length) return 1;
        using var document = JsonDocument.Parse(buffer.AsMemory(0, total), new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "ok" &&
               root.TryGetProperty("contract", out var contract) && contract.ValueKind == JsonValueKind.String && contract.GetString() == "m0-004/v1" ? 0 : 1;
    }
    catch (HttpRequestException) { return 1; }
    catch (OperationCanceledException) { return 1; }
    catch (IOException) { return 1; }
    catch (JsonException) { return 1; }
    catch (UriFormatException) { return 1; }
}
