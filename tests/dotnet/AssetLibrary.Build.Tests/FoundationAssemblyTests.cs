using System.Reflection;

namespace AssetLibrary.Build.Tests;

[TestClass]
public sealed class FoundationAssemblyTests
{
    [TestMethod]
    [DataRow("AssetLibrary.CoreServer", "CoreServer")]
    [DataRow("AssetLibrary.WorkerSupervisor", "WorkerSupervisor")]
    public void FoundationAssemblyIsBuildableButHasNoRuntimeEntryPoint(
        string assemblyName,
        string expectedRole)
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");

        Assert.IsTrue(File.Exists(assemblyPath), $"Missing build output: {assemblyPath}");
        var assembly = Assembly.LoadFrom(assemblyPath);
        var role = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "AssetLibrary.FoundationRole");

        Assert.IsNull(assembly.EntryPoint, $"{assemblyName} must remain inert in V01-001.");
        Assert.IsNotNull(role, $"{assemblyName} must declare its build-foundation role.");
        Assert.AreEqual(expectedRole, role.Value);
    }
}
