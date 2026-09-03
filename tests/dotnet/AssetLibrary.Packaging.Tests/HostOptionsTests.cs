using System.Net;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class HostOptionsTests
{
    [TestMethod]
    public void RunRequiresExistingAbsoluteNonRootStatePath()
    {
        var missing = CoreServerHostOptions.Parse([], _ => null);
        var relative = CoreServerHostOptions.Parse(["--state-path", "relative"], _ => null);
        var root = CoreServerHostOptions.Parse(["--state-path", Path.GetPathRoot(Environment.SystemDirectory)!], _ => null);
        var unavailable = CoreServerHostOptions.Parse(
            ["--state-path", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}")],
            _ => null);

        Assert.AreEqual("invalid_state_path", missing.ErrorCode);
        Assert.AreEqual("invalid_state_path", relative.ErrorCode);
        Assert.AreEqual("invalid_state_path", root.ErrorCode);
        Assert.AreEqual("state_path_unavailable", unavailable.ErrorCode);
    }

    [TestMethod]
    public void CommandLineOverridesEnvironmentWithoutAcceptingUnknownOrDuplicateValues()
    {
        using var state = TemporaryDirectory.Create();
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASSETLIBRARY_STATE_PATH"] = state.Path,
            ["ASSETLIBRARY_ENVIRONMENT"] = "Production",
            ["ASSETLIBRARY_PORT"] = "6000",
        };

        var result = CoreServerHostOptions.Parse(
            ["--port", "6001", "--bind-host", "127.0.0.1"],
            key => environment.GetValueOrDefault(key));
        var unknown = CoreServerHostOptions.Parse(["--wat"], _ => null);
        var duplicate = CoreServerHostOptions.Parse(
            ["--port", "6001", "--port", "6002"],
            _ => null);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(6001, result.Options!.Port);
        Assert.AreEqual(IPAddress.Loopback, result.Options.BindAddress);
        Assert.AreEqual("unknown_argument", unknown.ErrorCode);
        Assert.AreEqual("duplicate_argument", duplicate.ErrorCode);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("80")]
    [DataRow("65536")]
    [DataRow("+5080")]
    [DataRow("five")]
    public void PortMustStayInsideUnprivilegedDecimalRange(string value)
    {
        using var state = TemporaryDirectory.Create();
        var result = CoreServerHostOptions.Parse(
            ["--state-path", state.Path, "--environment", "Production", "--port", value],
            _ => null);

        Assert.AreEqual("invalid_port", result.ErrorCode);
    }

    [TestMethod]
    public void BindAndProbeAddressesAreLiteralAndProbeRemainsLoopback()
    {
        using var state = TemporaryDirectory.Create();
        var publicBind = CoreServerHostOptions.Parse(
            ["--state-path", state.Path, "--environment", "Production", "--bind-host", "0.0.0.0"],
            _ => null);
        var dnsBind = CoreServerHostOptions.Parse(
            ["--state-path", state.Path, "--environment", "Production", "--bind-host", "localhost"],
            _ => null);
        var remoteProbe = CoreServerHostOptions.Parse(
            ["--state-path", state.Path, "--environment", "Production", "--probe-host", "192.0.2.1"],
            _ => null);

        Assert.IsTrue(publicBind.IsValid);
        Assert.AreEqual(IPAddress.Any, publicBind.Options!.BindAddress);
        Assert.AreEqual("invalid_bind_host", dnsBind.ErrorCode);
        Assert.AreEqual("invalid_probe_host", remoteProbe.ErrorCode);
    }

    [TestMethod]
    public void BuildInfoCannotBeCombinedAndHealthProbeNeedsNoStatePath()
    {
        var buildInfo = CoreServerHostOptions.Parse(
            ["--build-info"],
            key => key == "ASSETLIBRARY_PORT" ? "invalid" : null);
        var combined = CoreServerHostOptions.Parse(
            ["--build-info", "--port", "5081"],
            _ => null);
        var probe = CoreServerHostOptions.Parse(["--health-probe", "--port", "5081"], _ => null);

        Assert.IsTrue(buildInfo.IsValid);
        Assert.AreEqual(CoreServerHostCommand.BuildInfo, buildInfo.Options!.Command);
        Assert.AreEqual("build_info_with_configuration", combined.ErrorCode);
        Assert.IsTrue(probe.IsValid);
        Assert.AreEqual(CoreServerHostCommand.HealthProbe, probe.Options!.Command);
    }

    [TestMethod]
    public void RunRequiresExplicitProductionEnvironment()
    {
        using var state = TemporaryDirectory.Create();
        var missing = CoreServerHostOptions.Parse(["--state-path", state.Path], _ => null);
        var development = CoreServerHostOptions.Parse(
            ["--state-path", state.Path, "--environment", "Development"],
            _ => null);
        var production = CoreServerHostOptions.Parse(
            ["--state-path", state.Path, "--environment", "Production"],
            _ => null);

        Assert.AreEqual("invalid_environment", missing.ErrorCode);
        Assert.AreEqual("invalid_environment", development.ErrorCode);
        Assert.IsTrue(production.IsValid);
        Assert.AreEqual("Production", production.Options!.EnvironmentName);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    private const string MarkerValue = "AssetLibrary/V01-008/release-output/v1\n";
    private const string MarkerName = ".assetlibrary-v01-008-dotnet-test";
    private static readonly object MarkerLock = new();

    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryDirectory Create()
    {
        var repository = FindRepositoryRoot();
        var taskRoot = System.IO.Path.Combine(
            repository,
            ".runtime",
            "sandbox-storage",
            "V01-008",
            "dotnet-tests");
        Directory.CreateDirectory(taskRoot);
        lock (MarkerLock)
        {
            var marker = System.IO.Path.Combine(taskRoot, MarkerName);
            if (File.Exists(marker) && File.ReadAllText(marker) != MarkerValue)
            {
                throw new InvalidOperationException("V01-008 test-root marker does not match.");
            }

            File.WriteAllText(marker, MarkerValue);
        }
        var path = System.IO.Path.Combine(taskRoot, $"assetlibrary-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        File.WriteAllText(System.IO.Path.Combine(path, MarkerName), MarkerValue);
        return new TemporaryDirectory(path);
    }

    public void Dispose()
    {
        var taskRoot = Directory.GetParent(Path)!.FullName;
        var prefix = System.IO.Path.GetFullPath(taskRoot).TrimEnd(
            System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var fullPath = System.IO.Path.GetFullPath(Path);
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || File.ReadAllText(System.IO.Path.Combine(fullPath, MarkerName)) != MarkerValue
            || (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Refusing unsafe V01-008 test cleanup.");
        }

        Directory.Delete(fullPath, recursive: true);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(System.IO.Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root for test isolation.");
    }
}
