using System.Globalization;

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

    [TestMethod]
    [DataRow(128)]
    [DataRow(129)]
    public void ImageCountAccepts128AndRejects129(int count)
    {
        var staged = Path.Combine(runtime, "native-image-fixtures");
        Directory.CreateDirectory(staged);
        for (var index = 1; index <= count; index++)
        {
            File.WriteAllBytes(Path.Combine(staged, index.ToString("D3", CultureInfo.InvariantCulture) + ".png"), [42]);
        }
        if (count == 129)
        {
            Assert.Throws<AssertFailedException>(() => new NativeClientSampleAssets(runtime, staged));
            Assert.IsFalse(Directory.Exists(Path.Combine(runtime, "assets", "native", "图片样例")));
            return;
        }
        var assets = new NativeClientSampleAssets(runtime, staged);
        Assert.AreEqual(266, assets.FileCount);
        Assert.AreEqual(128, Directory.EnumerateFiles(Path.Combine(assets.LibraryRoot, "图片样例")).Count());
        assets.VerifyUnchanged();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DirectFixtureCopyEnforces64MiBWithoutPython(bool extraByte)
    {
        var staged = Path.Combine(runtime, "native-image-fixtures");
        Directory.CreateDirectory(staged);
        CreateSizedFile(Path.Combine(staged, "first.png"), 33554432);
        CreateSizedFile(Path.Combine(staged, "second.png"), 33554432);
        if (extraByte) { CreateSizedFile(Path.Combine(staged, "third.png"), 1); }
        var stamps = Directory.EnumerateFiles(staged).ToDictionary(path => path, File.GetLastWriteTimeUtc);
        if (extraByte)
        {
            Assert.Throws<AssertFailedException>(() => new NativeClientSampleAssets(runtime, staged));
        }
        else
        {
            var assets = new NativeClientSampleAssets(runtime, staged);
            Assert.AreEqual(140, assets.FileCount);
            assets.VerifyUnchanged();
        }
        var copied = Path.Combine(runtime, "assets", "native", "图片样例");
        var copiedBytes = Directory.EnumerateFiles(copied).Sum(path => new FileInfo(path).Length);
        if (extraByte) { Assert.IsLessThanOrEqualTo(67108864L, copiedBytes); }
        else { Assert.AreEqual(67108864, copiedBytes); }
        foreach (var (path, stamp) in stamps) { Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(path)); }
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(33554433L)]
    public void DirectFixtureCopyStillRejectsInvalidSingleFileLength(long length)
    {
        var staged = Path.Combine(runtime, "native-image-fixtures");
        Directory.CreateDirectory(staged);
        CreateSizedFile(Path.Combine(staged, "oversized.png"), length);
        Assert.Throws<AssertFailedException>(() => new NativeClientSampleAssets(runtime, staged));
        Assert.IsFalse(File.Exists(Path.Combine(runtime, "assets", "native", "图片样例", "oversized.png")));
    }

    private static void CreateSizedFile(string path, long size)
    {
        using var stream = File.Create(path);
        stream.SetLength(size);
    }
}
