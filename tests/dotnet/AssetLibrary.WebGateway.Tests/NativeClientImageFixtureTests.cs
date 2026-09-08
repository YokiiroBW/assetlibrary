namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class NativeClientImageFixtureTests
{
    private string runtime = null!;

    [TestInitialize]
    public void CreatePrivateRuntime() => runtime = Directory.CreateTempSubdirectory("AssetLibrary-V03-005-images-").FullName;

    [TestCleanup]
    public void DeletePrivateRuntime() => Directory.Delete(runtime, recursive: true);

    [TestMethod]
    public void DefaultCorpusKeepsItsExistingCountAndUnchangedCheck()
    {
        var assets = new NativeClientSampleAssets(runtime);
        Assert.AreEqual(138, assets.FileCount);
        Assert.IsFalse(Directory.Exists(Path.Combine(assets.LibraryRoot, "图片样例")));
        assets.VerifyUnchanged();
    }

    [TestMethod]
    public void ImageCopiesPreserveNestedNamesAndParticipateInSourceVerification()
    {
        var staged = Path.Combine(runtime, "native-image-fixtures");
        Directory.CreateDirectory(Path.Combine(staged, "中文目录"));
        var original = Path.Combine(staged, "中文目录", "sample.dat");
        File.WriteAllText(original, "Synthetic fixture content, not a decoder success assertion.");
        var originalStamp = File.GetLastWriteTimeUtc(original);
        var assets = new NativeClientSampleAssets(runtime, staged);
        var copied = Path.Combine(assets.LibraryRoot, "图片样例", "中文目录", "sample.dat");
        Assert.AreEqual(139, assets.FileCount);
        Assert.AreEqual(File.ReadAllText(original), File.ReadAllText(copied));
        Assert.AreEqual(originalStamp, File.GetLastWriteTimeUtc(original));
        assets.VerifyUnchanged();
        File.AppendAllText(copied, "changed");
        Assert.Throws<AssertFailedException>(assets.VerifyUnchanged);
    }

    [TestMethod]
    public void ImageModeRejectsAnUnstagedDirectory()
    {
        var unstaged = Path.Combine(runtime, "unverified");
        Directory.CreateDirectory(unstaged);
        File.WriteAllText(Path.Combine(unstaged, "sample.dat"), "Synthetic unverified input.");
        Assert.Throws<AssertFailedException>(() => new NativeClientSampleAssets(runtime, unstaged));
    }
}
