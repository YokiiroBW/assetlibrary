using System.Globalization;
using System.Security.Cryptography;

namespace AssetLibrary.WebGateway.Tests;

internal sealed class NativeClientSampleAssets
{
    private readonly Dictionary<string, (byte[] Digest, DateTime Modified)> snapshots = [];

    public NativeClientSampleAssets(string temporaryRoot)
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
}
