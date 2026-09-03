namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class SandboxBoundaryTests
{
    [TestMethod]
    public void SandboxBaseItselfIsNotAnAllowedFixture()
    {
        using var fixture = new SandboxFixture();
        var sandboxBase = Path.Combine(
            fixture.RepositoryRoot,
            ".runtime",
            "sandbox-storage",
            "V01-007");

        Assert.ThrowsExactly<InvalidOperationException>(
            () => SandboxPathBoundary.Open(fixture.RepositoryRoot, sandboxBase));
    }

    [TestMethod]
    public void RelativeTraversalIsRejectedBeforeUse()
    {
        using var fixture = new SandboxFixture();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => fixture.Register("escape_token", "../outside.bin"));
    }

    [TestMethod]
    public void ReparseComponentIsRejected()
    {
        using var fixture = new SandboxFixture();
        var real = fixture.Boundary.ResolveRelative("real");
        var link = fixture.Boundary.ResolveRelative("link");
        Directory.CreateDirectory(real);
        SandboxReparsePoint.CreateDirectoryLink(link, real);
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(
                () => fixture.Register("link_token", Path.Combine("link", "payload.bin")));
        }
        finally
        {
            Directory.Delete(link);
        }
    }
}
