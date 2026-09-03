using System.Reflection;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class HostContractTests
{
    [TestMethod]
    public void ReadyPayloadMakesItsNarrowScopeExplicit()
    {
        var health = CoreServerEndpointPayloads.Health();
        var ready = CoreServerEndpointPayloads.Ready();

        Assert.AreEqual("ok", health.Status);
        Assert.AreEqual(CoreServerBuildInfo.CurrentContract, health.Contract);
        Assert.AreEqual("ready", ready.Status);
        Assert.AreEqual("host_only", ready.Scope);
        Assert.IsFalse(ready.BusinessApiReady);
        Assert.IsFalse(ready.ProductionFileWritesEnabled);
    }

    [TestMethod]
    public void BuildInfoContainsOnlyBoundedNonHostSpecificFacts()
    {
        var info = CoreServerBuildInfo.Read(typeof(CoreServerBuildInfo).Assembly);
        var json = CoreServerHostJson.Serialize(info);

        Assert.AreEqual(CoreServerBuildInfo.CurrentContract, info.Contract);
        Assert.AreNotEqual(string.Empty, info.SourceRevision);
        Assert.AreNotEqual(string.Empty, info.InformationalVersion);
        Assert.AreNotEqual(string.Empty, info.Framework);
        Assert.AreNotEqual(string.Empty, info.RuntimeIdentifier);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AppContext.BaseDirectory, json, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task StateBoundaryProbeWritesAndLeavesNoFileBehind()
    {
        using var state = TemporaryDirectory.Create();

        Assert.IsTrue(await CoreServerStateBoundary.IsWritableAsync(state.Path));
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(state.Path)
            .Any(path => Path.GetFileName(path).StartsWith(
                ".assetlibrary-host-write-probe",
                StringComparison.Ordinal)));
        Assert.IsFalse(await CoreServerStateBoundary.IsWritableAsync(
            Path.Combine(state.Path, "missing")));
    }

    [TestMethod]
    public void HostWrapperHasEntryPointAndCoreLibraryRemainsInert()
    {
        var host = typeof(CoreServerBuildInfo).Assembly;
        var core = Assembly.Load("AssetLibrary.CoreServer");

        Assert.IsNotNull(host.EntryPoint);
        Assert.IsNull(core.EntryPoint);
        Assert.AreEqual(
            "CoreServer",
            host.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "AssetLibrary.HostRole")
                .Value);
    }

    [TestMethod]
    public void ErrorsUseStableCodesWithoutExceptionOrPathFields()
    {
        var json = CoreServerHostJson.Error("invalid_state_path");

        StringAssert.Contains(json, "\"level\":\"error\"");
        StringAssert.Contains(json, "\"code\":\"invalid_state_path\"");
        Assert.DoesNotContain("\"path\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"exception\":", json, StringComparison.OrdinalIgnoreCase);
    }
}
