
namespace AssetLibrary.ReadCore.Tests;

/// <summary>
/// Documents where the path boundary actually is: the shared relative-path contract refuses a
/// traversal, an absolute or a drive-qualified name before any reader sees it, and the reader's own
/// root check is the second line of defence that resolves only inside its allowed root.
/// </summary>
[TestClass]
public sealed class DedupPathContractTests
{
    [TestMethod]
    public void ARootCheckResolvesOnlyInsideItsOwnRoot()
    {
        using var scenario = new DedupScenario();
        scenario.WriteText("confined/legit.bin", "content");
        var root = scenario.UnregisteredRoot("confined");

        var direct = DedupPathGuard.Resolve(root, new RelativeAssetPath("legit.bin"));
        Assert.IsNotNull(direct);
        Assert.StartsWith(scenario.AbsolutePath("confined"), direct);

        var nested = DedupPathGuard.Resolve(root, new RelativeAssetPath("sub/child.bin"));
        Assert.IsNotNull(nested);
        Assert.StartsWith(scenario.AbsolutePath("confined"), nested);
    }

    [TestMethod]
    public void TheSharedPathContractRejectsAbsoluteTraversalAndDrivePaths()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelativeAssetPath("sub/../secret.bin"));
        Assert.ThrowsExactly<ArgumentException>(() => new RelativeAssetPath("/etc/hosts"));
        Assert.ThrowsExactly<ArgumentException>(() => new RelativeAssetPath("C:/outside/secret.bin"));
        Assert.ThrowsExactly<ArgumentException>(() => new RelativeAssetPath("C:secret.bin"));
    }
}
