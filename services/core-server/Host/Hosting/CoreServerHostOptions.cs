using System.Globalization;
using System.Net;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed record CoreServerHostOptions(
    CoreServerHostCommand Command,
    string StatePath,
    string EnvironmentName,
    IPAddress BindAddress,
    IPAddress ProbeAddress,
    int Port)
{
    public const int DefaultPort = 5080;
    public const int MinimumPort = 1024;
    public const int MaximumPort = 65535;

    private const string StatePathEnvironment = "ASSETLIBRARY_STATE_PATH";
    private const string EnvironmentEnvironment = "ASSETLIBRARY_ENVIRONMENT";
    private const string BindHostEnvironment = "ASSETLIBRARY_BIND_HOST";
    private const string ProbeHostEnvironment = "ASSETLIBRARY_PROBE_HOST";
    private const string PortEnvironment = "ASSETLIBRARY_PORT";

    public static CoreServerHostOptionsResult Parse(
        IReadOnlyList<string> arguments,
        Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);

        var command = CoreServerHostCommand.Run;
        var commandSeen = false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is "--health-probe" or "--build-info")
            {
                if (commandSeen)
                {
                    return CoreServerHostOptionsResult.Invalid("multiple_commands");
                }

                command = argument == "--health-probe"
                    ? CoreServerHostCommand.HealthProbe
                    : CoreServerHostCommand.BuildInfo;
                commandSeen = true;
                continue;
            }

            if (argument is not ("--state-path" or "--environment" or "--bind-host" or "--probe-host" or "--port"))
            {
                return CoreServerHostOptionsResult.Invalid("unknown_argument");
            }

            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return CoreServerHostOptionsResult.Invalid("missing_argument_value");
            }

            if (!values.TryAdd(argument, arguments[++index]))
            {
                return CoreServerHostOptionsResult.Invalid("duplicate_argument");
            }
        }

        if (command == CoreServerHostCommand.BuildInfo && values.Count != 0)
        {
            return CoreServerHostOptionsResult.Invalid("build_info_with_configuration");
        }

        if (command == CoreServerHostCommand.BuildInfo)
        {
            return CoreServerHostOptionsResult.Valid(new CoreServerHostOptions(
                command,
                string.Empty,
                string.Empty,
                IPAddress.Loopback,
                IPAddress.Loopback,
                DefaultPort));
        }

        var portResult = ParsePort(Value(values, "--port", PortEnvironment, environment));
        if (!portResult.IsValid)
        {
            return CoreServerHostOptionsResult.Invalid(portResult.ErrorCode);
        }

        var bindResult = ParseAddress(
            Value(values, "--bind-host", BindHostEnvironment, environment) ?? IPAddress.Loopback.ToString(),
            allowAny: true);
        if (!bindResult.IsValid)
        {
            return CoreServerHostOptionsResult.Invalid("invalid_bind_host");
        }

        var probeResult = ParseAddress(
            Value(values, "--probe-host", ProbeHostEnvironment, environment) ?? IPAddress.Loopback.ToString(),
            allowAny: false);
        if (!probeResult.IsValid)
        {
            return CoreServerHostOptionsResult.Invalid("invalid_probe_host");
        }

        var runConfiguration = ParseRunConfiguration(command, values, environment);
        if (!runConfiguration.IsValid)
        {
            return CoreServerHostOptionsResult.Invalid(runConfiguration.ErrorCode);
        }

        return CoreServerHostOptionsResult.Valid(new CoreServerHostOptions(
            command,
            runConfiguration.StatePath,
            runConfiguration.EnvironmentName,
            bindResult.Address!,
            probeResult.Address!,
            portResult.Port));
    }

    private static RunConfigurationParseResult ParseRunConfiguration(
        CoreServerHostCommand command,
        IReadOnlyDictionary<string, string> values,
        Func<string, string?> environment)
    {
        if (command != CoreServerHostCommand.Run)
        {
            return RunConfigurationParseResult.Valid(string.Empty, string.Empty);
        }

        var pathResult = ParseStatePath(Value(values, "--state-path", StatePathEnvironment, environment));
        if (!pathResult.IsValid)
        {
            return RunConfigurationParseResult.Invalid(pathResult.ErrorCode);
        }

        var environmentName = Value(values, "--environment", EnvironmentEnvironment, environment);
        if (!string.Equals(environmentName, "Production", StringComparison.Ordinal))
        {
            return RunConfigurationParseResult.Invalid("invalid_environment");
        }

        return RunConfigurationParseResult.Valid(pathResult.Value, environmentName!);
    }

    private static string? Value(
        IReadOnlyDictionary<string, string> values,
        string argumentName,
        string environmentName,
        Func<string, string?> environment) =>
        values.TryGetValue(argumentName, out var value) ? value : environment(environmentName);

    private static PortParseResult ParsePort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PortParseResult.Valid(DefaultPort);
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is >= MinimumPort and <= MaximumPort
            ? PortParseResult.Valid(port)
            : PortParseResult.Invalid("invalid_port");
    }

    private static AddressParseResult ParseAddress(string value, bool allowAny)
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            return AddressParseResult.Invalid();
        }

        var permitted = IPAddress.IsLoopback(address)
            || (allowAny && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)));
        return permitted ? AddressParseResult.Valid(address) : AddressParseResult.Invalid();
    }

    private static StatePathParseResult ParseStatePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            return StatePathParseResult.Invalid("invalid_state_path");
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)
                || string.Equals(
                    Path.TrimEndingDirectorySeparator(fullPath),
                    Path.TrimEndingDirectorySeparator(root),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return StatePathParseResult.Invalid("invalid_state_path");
            }

            if (!Directory.Exists(fullPath))
            {
                return StatePathParseResult.Invalid("state_path_unavailable");
            }

            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                return StatePathParseResult.Invalid("state_path_reparse_point");
            }

            return StatePathParseResult.Valid(fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            return StatePathParseResult.Invalid("state_path_unavailable");
        }
    }

    private readonly record struct PortParseResult(bool IsValid, int Port, string ErrorCode)
    {
        public static PortParseResult Valid(int port) => new(true, port, string.Empty);

        public static PortParseResult Invalid(string errorCode) => new(false, 0, errorCode);
    }

    private readonly record struct AddressParseResult(bool IsValid, IPAddress? Address)
    {
        public static AddressParseResult Valid(IPAddress address) => new(true, address);

        public static AddressParseResult Invalid() => new(false, null);
    }

    private readonly record struct StatePathParseResult(bool IsValid, string Value, string ErrorCode)
    {
        public static StatePathParseResult Valid(string value) => new(true, value, string.Empty);

        public static StatePathParseResult Invalid(string errorCode) => new(false, string.Empty, errorCode);
    }

    private readonly record struct RunConfigurationParseResult(
        bool IsValid,
        string StatePath,
        string EnvironmentName,
        string ErrorCode)
    {
        public static RunConfigurationParseResult Valid(string statePath, string environmentName) =>
            new(true, statePath, environmentName, string.Empty);

        public static RunConfigurationParseResult Invalid(string errorCode) =>
            new(false, string.Empty, string.Empty, errorCode);
    }
}

internal sealed record CoreServerHostOptionsResult(
    bool IsValid,
    CoreServerHostOptions? Options,
    string ErrorCode)
{
    public static CoreServerHostOptionsResult Valid(CoreServerHostOptions options) =>
        new(true, options, string.Empty);

    public static CoreServerHostOptionsResult Invalid(string errorCode) =>
        new(false, null, errorCode);
}
