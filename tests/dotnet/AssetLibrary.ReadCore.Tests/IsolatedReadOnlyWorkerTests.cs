using System.Text.Json;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class IsolatedReadOnlyWorkerProtocolTests
{
    [TestMethod]
    public async Task ProbeAndDiscoveryProtocolRequireACompleteBoundedRequest()
    {
        using var invalid = new StringReader("{\"version\":2,\"canonicalRoot\":\"C:/sandbox\",\"caseSensitive\":false}\n");
        using var output = new StringWriter();
        Assert.AreNotEqual(0, await LibraryRootProbeWorker.RunAsync(invalid, output, CancellationToken.None));
        using var oversized = new StringReader(new string('x', 16 * 1024 + 1) + "\n");
        using var scanOutput = new StringWriter();
        Assert.AreNotEqual(0, await ReadOnlyScanWorker.RunAsync(oversized, scanOutput, CancellationToken.None));
        Assert.IsTrue(scanOutput.ToString().Contains("failure", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WorkerStreamsACompleteSnapshotWithoutChangingAnyAsset()
    {
        using var sandbox = new RepositorySandbox();
        var directory = Directory.CreateDirectory(Path.Combine(sandbox.Root, "照片"));
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "image.jpg"), "asset");
        var before = sandbox.CaptureStrongSnapshot();
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        { ["version"] = 1, ["canonicalRoot"] = sandbox.Root, ["caseSensitive"] = !OperatingSystem.IsWindows() });
        using var input = new StringReader(json + "\n");
        using var output = new StringWriter();
        Assert.AreEqual(0, await ReadOnlyScanWorker.RunAsync(input, output, CancellationToken.None));
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(3, lines);
        using var terminal = JsonDocument.Parse(lines[^1]);
        Assert.AreEqual("complete", terminal.RootElement.GetProperty("type").GetString());
        Assert.AreEqual(2, terminal.RootElement.GetProperty("observedEntries").GetInt32());
        Assert.AreEqual(before, sandbox.CaptureStrongSnapshot());
    }

}
