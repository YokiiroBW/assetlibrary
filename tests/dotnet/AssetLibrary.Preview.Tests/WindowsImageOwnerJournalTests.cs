using System.Runtime.Versioning;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.ReadCore.Tests;

namespace AssetLibrary.Preview.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsImageOwnerJournalTests
{
    [TestInitialize]
    public void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows owner journal boundary is required.");
    }

    [TestMethod]
    [DataRow("3", "123", "-1")]
    [DataRow("3", "123", "+1")]
    [DataRow("3", "123", "01")]
    [DataRow("3", "123", " 1")]
    [DataRow("3", "123", "1 ")]
    [DataRow("3", "123", "0")]
    [DataRow("3", "0", "1")]
    [DataRow("3", "+123", "1")]
    [DataRow("3", "0123", "1")]
    [DataRow("-1", "123", "1")]
    [DataRow("+3", "123", "1")]
    [DataRow("03", "123", "1")]
    public void RejectsNegativeSignedAndNoncanonicalIdentity(string phase, string pid, string creation)
    {
        using var sandbox = new RepositorySandbox();
        var record = Write(sandbox.Root, phase, pid, creation);
        var failure = Assert.ThrowsExactly<ReadOnlyWorkerException>(() => WindowsImageOwnerJournal.Read(record.Path, record.Name));
        Assert.AreEqual("preview_owner_record_invalid", failure.Code);
    }

    [TestMethod]
    [DataRow("1", "0", "0")]
    [DataRow("3", "123", "1")]
    public void AcceptsValidCreatedAndStartedIdentity(string phase, string pid, string creation)
    {
        using var sandbox = new RepositorySandbox();
        var record = Write(sandbox.Root, phase, pid, creation);
        var result = WindowsImageOwnerJournal.Read(record.Path, record.Name);
        Assert.IsGreaterThanOrEqualTo(0L, result.Creation);
    }

    private static (string Path, string Name) Write(string root, string phase, string pid, string creation)
    {
        var name = "alimg2." + Guid.NewGuid().ToString("N");
        var path = Path.Combine(root, name + ".owner");
        File.WriteAllText(path, $"{WindowsImageOwnerJournal.Version}\n{name}\n{phase}\n{pid}\n{creation}\n");
        return (path, name);
    }
}
