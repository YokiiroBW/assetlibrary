using System.Security.Cryptography;
using System.Text;

namespace AssetLibrary.WebGateway.Tests;

internal sealed class TrialHostIntegrationAssets
{
    private readonly Dictionary<string, (string Hash, DateTime Modified)> originals = [];
    private readonly string assetRoot;

    public TrialHostIntegrationAssets(string runtimeRoot)
    {
        assetRoot = Path.Combine(runtimeRoot, "assets");
        LibraryRoot = Path.Combine(assetRoot, "library");
        Directory.CreateDirectory(Path.Combine(LibraryRoot, "album"));
        Add("cover.txt", "Read-only trial original content.");
        Add("album/summer-photo.jpg", "Synthetic file bytes; never decoded or modified.");
        Add("album/notes_中文.md", "Isolated international path fixture.");
    }

    public string LibraryRoot { get; }

    public void AssertUnchanged()
    {
        foreach (var (path, original) in originals)
        {
            Assert.AreEqual(original.Hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            Assert.AreEqual(original.Modified, File.GetLastWriteTimeUtc(path));
        }

        Assert.HasCount(originals.Count, Directory.GetFiles(assetRoot, "*", SearchOption.AllDirectories));
    }

    public string AdditionalLibrary(string name)
    {
        Assert.IsTrue(name.All(char.IsAsciiLetterLower));
        var root = Directory.CreateDirectory(Path.Combine(assetRoot, name)).FullName;
        var file = Path.Combine(root, "sample.txt");
        File.WriteAllText(file, "Isolated retry/cancel source.");
        originals[file] = (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), File.GetLastWriteTimeUtc(file));
        return root;
    }

    public void SetOffline(string root, bool offline)
    {
        Assert.AreEqual(Path.GetFullPath(assetRoot), Path.GetDirectoryName(Path.GetFullPath(root)));
        var moved = root + ".offline";
        Directory.Move(offline ? root : moved, offline ? moved : root);
    }

    private void Add(string relative, string content)
    {
        var path = Path.Combine(LibraryRoot, relative);
        File.WriteAllText(path, content, Encoding.UTF8);
        originals[path] = (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), File.GetLastWriteTimeUtc(path));
    }
}
