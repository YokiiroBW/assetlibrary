using System.Diagnostics;
using AssetLibrary.Windows.Setup;

namespace AssetLibrary.Windows.Setup.Tests;

[TestClass]
public sealed class PackageValidationTests : SetupTestBase
{
    [TestMethod]
    public async Task ReparsePointInsidePayloadIsRejected()
    {
        string package = await PackageAsync();
        string target = Path.Combine(Sandbox, "outside");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "foreign.txt"), "preserve");
        string link = Path.Combine(package, "payload", "linked");
        using (Process junction = new()
        {
            StartInfo = new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "mklink", "/J", link, target },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        })
        {
            Assert.IsTrue(junction.Start());
            Assert.IsTrue(junction.WaitForExit(5000));
            Assert.AreEqual(0, junction.ExitCode);
        }
        try
        {
            SetupException error = await Assert.ThrowsExactlyAsync<SetupException>(() => CreateInstaller().InstallAsync(package, CancellationToken.None));
            Assert.AreEqual("reparse_point", error.Code);
            Assert.IsFalse(Directory.Exists(Root));
            Assert.AreEqual("preserve", await File.ReadAllTextAsync(Path.Combine(target, "foreign.txt")));
        }
        finally { Directory.Delete(link); }
    }

    [TestMethod]
    public async Task CorruptedPayloadLeavesNoInstallationOrRegistration()
    {
        string package = await PackageAsync();
        await File.AppendAllTextAsync(Path.Combine(package, "payload", "AssetLibrary.Explorer.dll"), "damage");
        await Assert.ThrowsExactlyAsync<SetupException>(() => CreateInstaller().InstallAsync(package, CancellationToken.None));
        Assert.IsFalse(Directory.Exists(Root));
        Assert.IsEmpty(Store.Read().Keys);
    }

    [TestMethod]
    public async Task ExtraFileIsRejectedBeforeCopy()
    {
        string package = await PackageAsync();
        await File.WriteAllTextAsync(Path.Combine(package, "payload", "unlisted.dll"), "extra");
        SetupException error = await Assert.ThrowsExactlyAsync<SetupException>(() => CreateInstaller().InstallAsync(package, CancellationToken.None));
        Assert.AreEqual("unexpected_file", error.Code);
        Assert.IsFalse(Directory.Exists(Root));
    }

    [TestMethod]
    [DataRow("../escape.dll")]
    [DataRow("C:/escape.dll")]
    [DataRow("nested/../../escape.dll")]
    [DataRow("nested\\escape.dll")]
    [DataRow("nested/file.dll:stream")]
    [DataRow("CON.dll")]
    [DataRow("nested /file.dll")]
    [DataRow("nested/file.dll.")]
    public void UnsafeManifestPathsAreRejected(string path)
    {
        Assert.ThrowsExactly<SetupException>(() => SafePath.Resolve(Sandbox, path));
    }

    [TestMethod]
    public async Task CaseInsensitiveDuplicateManifestIsRejected()
    {
        string package = await PackageAsync();
        PackageManifest manifest = await ManifestAsync(package);
        manifest = manifest with { Files = [.. manifest.Files, manifest.Files[0] with { Path = manifest.Files[0].Path.ToUpperInvariant() }] };
        StateFile.Write(Path.Combine(package, Product.ManifestName), manifest);
        SetupException error = await Assert.ThrowsExactlyAsync<SetupException>(() => CreateInstaller().InstallAsync(package, CancellationToken.None));
        Assert.AreEqual("invalid_manifest", error.Code);
    }

    [TestMethod]
    public void SandboxCommandCannotTargetRealProgramDirectories()
    {
        Assert.ThrowsExactly<SetupException>(() => SetupOptions.Parse(["install", "--sandbox", Product.DefaultRoot]));
        SetupOptions options = SetupOptions.Parse(["status", "--sandbox", Sandbox]);
        Assert.IsTrue(options.Quiet);
        Assert.AreEqual(Sandbox, options.Sandbox);
    }

}
