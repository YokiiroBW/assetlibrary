using System.Globalization;
using System.Security.Cryptography;

namespace AssetLibrary.WebGateway.Tests;

internal sealed class NativeClientSampleAssets
{
    private readonly Dictionary<string, (byte[] Digest, DateTime Modified)> snapshots = [];

    public NativeClientSampleAssets(string temporaryRoot, string? imageFixtures = null)
    {
        LibraryRoot = Path.Combine(temporaryRoot, "assets", "native");
        Directory.CreateDirectory(Path.Combine(LibraryRoot, "相册", "夏日"));
        for (var number = 1; number <= 135; number++)
        {
            var name = "sample-" + number.ToString("D3", CultureInfo.InvariantCulture) + ".txt";
            CreateSample(name, "Synthetic native client fixture " + number.ToString(CultureInfo.InvariantCulture));
        }

        CreateSample("说明_中文.md", "# 隔离测试\n仅用于原生客户端浏览与检索。\n");
        CreateSample("empty.txt", "");
        CreateSample("相册/夏日/preview-placeholder.jpg", "Synthetic bytes; no image or content preview is provided.");
        if (imageFixtures is not null)
        {
            AddImageFixtures(temporaryRoot, imageFixtures);
        }
    }

    public string LibraryRoot { get; }
    public int FileCount => snapshots.Count;

    public void VerifyUnchanged()
    {
        Assert.AreEqual(snapshots.Count, Directory.EnumerateFiles(LibraryRoot, "*", SearchOption.AllDirectories).Count());
        foreach (var (filename, expected) in snapshots)
        {
            using var content = File.OpenRead(filename);
            CollectionAssert.AreEqual(expected.Digest, SHA256.HashData(content), "Synthetic source hash changed.");
            Assert.AreEqual(expected.Modified, File.GetLastWriteTimeUtc(filename), "Synthetic source timestamp changed.");
        }
    }

    private void CreateSample(string relativeName, string content)
    {
        var filename = Path.Combine(LibraryRoot, relativeName);
        File.WriteAllText(filename, content);
        using var stream = File.OpenRead(filename);
        snapshots.Add(filename, (SHA256.HashData(stream), File.GetLastWriteTimeUtc(filename)));
    }

    private void AddImageFixtures(string temporaryRoot, string imageFixtures)
    {
        var expected = Path.GetFullPath(Path.Combine(temporaryRoot, "native-image-fixtures"));
        Assert.AreEqual(expected, Path.GetFullPath(imageFixtures), "Images must be staged inside this fixture's private runtime.");
        Assert.AreEqual((FileAttributes)0, File.GetAttributes(expected) & FileAttributes.ReparsePoint);
        var images = Directory.EnumerateFiles(expected, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            MaxRecursionDepth = 16,
        }).Take(129).ToArray();
        Assert.IsTrue(images.Length is >= 1 and <= 128);
        long totalBytes = 0;
        foreach (var image in images)
        {
            Assert.AreEqual((FileAttributes)0, File.GetAttributes(image) & FileAttributes.ReparsePoint);
            var filename = Path.Combine(LibraryRoot, "图片样例", Path.GetRelativePath(expected, image));
            Directory.CreateDirectory(Path.GetDirectoryName(filename)!);
            CopyImage(image, filename, ref totalBytes);
        }
    }

    private void CopyImage(string image, string filename, ref long totalBytes)
    {
        using var source = File.OpenRead(image);
        Assert.IsTrue(source.Length is > 0 and <= 33554432);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long fileBytes = 0;
        using (var target = new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[65536];
            int read;
            while ((read = source.Read(buffer)) != 0)
            {
                fileBytes += read;
                totalBytes += read;
                Assert.IsTrue(fileBytes <= 33554432 && totalBytes <= 67108864, "Synthetic images exceed their byte budget.");
                digest.AppendData(buffer, 0, read);
                target.Write(buffer, 0, read);
            }
        }
        Assert.IsGreaterThan(0L, fileBytes);
        using var copied = File.OpenRead(filename);
        var copiedDigest = SHA256.HashData(copied);
        CollectionAssert.AreEqual(digest.GetHashAndReset(), copiedDigest, "Synthetic image copy hash changed.");
        snapshots.Add(filename, (copiedDigest, File.GetLastWriteTimeUtc(filename)));
    }
}
