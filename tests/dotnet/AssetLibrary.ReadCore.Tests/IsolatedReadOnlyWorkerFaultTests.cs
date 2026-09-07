using System.Diagnostics;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class IsolatedReadOnlyWorkerFaultTests
{
    [TestMethod]
    [DataRow("incomplete")]
    [DataRow("wrong_count")]
    [DataRow("nonzero")]
    [DataRow("oversized")]
    [DataRow("stderr_limit")]
    [DataRow("never_read")]
    public async Task FailedOrUnresponsiveProcessesCannotCommitAndAreReaped(string fault)
    {
        var python = ReadOnlyTrialFixture.RequiredEnvironment("ASSETLIBRARY_TEST_PYTHON");
        var script = ReadOnlyTrialFixture.RequiredEnvironment("ASSETLIBRARY_TEST_WORKER_FAULT_SCRIPT");
        using var sandbox = new RepositorySandbox();
        var pidFile = Path.Combine(sandbox.Root, "worker.pid");
        var options = new ReadOnlyWorkerProcessOptions(python, [script, fault, pidFile],
            TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(2));
        // This fills the anonymous pipe while staying below the internal request frame cap.
        var root = fault == "never_read" ? "C:/" + new string('a', 14000) : sandbox.Root;
        var started = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<FileDiscoveryException>(async () =>
        {
            await foreach (var _ in new ProcessReadOnlyFileDiscovery(options)
                .DiscoverAsync(IsolatedReadOnlyWorkerTraversalTests.Target(root), CancellationToken.None))
            {
            }
        });
        Assert.IsLessThan(TimeSpan.FromSeconds(5), started.Elapsed);
        if (OperatingSystem.IsWindows() && fault == "never_read")
        {
            Assert.AreEqual("worker_startup_timed_out", error.FailureCode);
        }

        var pid = int.Parse(await File.ReadAllTextAsync(pidFile), System.Globalization.CultureInfo.InvariantCulture);
        AssertProcessExited(pid);
    }

    private static void AssertProcessExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.IsTrue(process.HasExited, "The isolated worker must be reaped before the operation returns.");
        }
        catch (ArgumentException)
        {
            // An already reaped PID no longer has a process object.
        }
    }
}
