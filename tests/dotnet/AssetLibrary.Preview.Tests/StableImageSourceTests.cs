using System.Security.Cryptography;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.ReadCore.Tests;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class StableImageSourceTests
{
    [TestMethod]
    public void StreamsStableBytesAndRetainsOriginalMetadata()
    {
        using var fixture = new ImageSourceFixture();
        using var source = fixture.Open();
        using var output = new MemoryStream();
        var digest = source.CopyVerifiedTo(output, fixture.Bytes.Length, fixture.Modified, 4096, CancellationToken.None);
        CollectionAssert.AreEqual(fixture.Bytes, output.ToArray());
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(fixture.Bytes)), digest);
        source.VerifyCurrent(CancellationToken.None);
        CollectionAssert.AreEqual(fixture.Bytes, File.ReadAllBytes(fixture.Asset));
        Assert.AreEqual(fixture.Modified, new DateTimeOffset(File.GetLastWriteTimeUtc(fixture.Asset)));
    }

    [TestMethod]
    public void StaleIndexAndSizeLimitFailBeforeWritingTheCopy()
    {
        using var fixture = new ImageSourceFixture();
        using var source = fixture.Open();
        using var output = new MemoryStream();
        Assert.ThrowsExactly<ReadOnlyWorkerException>(() => source.CopyVerifiedTo(output,
            fixture.Bytes.Length + 1, fixture.Modified, 4096, CancellationToken.None));
        Assert.ThrowsExactly<ReadOnlyWorkerException>(() => source.CopyVerifiedTo(output,
            fixture.Bytes.Length, fixture.Modified.AddSeconds(-1), 4096, CancellationToken.None));
        Assert.ThrowsExactly<ReadOnlyWorkerException>(() => source.CopyVerifiedTo(output,
            fixture.Bytes.Length, fixture.Modified, 1, CancellationToken.None));
        Assert.AreEqual(0, output.Length);
    }

    [TestMethod]
    public void PersistedMicrosecondPrecisionDoesNotRejectAnUnchangedFile()
    {
        using var fixture = new ImageSourceFixture();
        var precise = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero).AddTicks(7);
        File.SetLastWriteTimeUtc(fixture.Asset, precise.UtcDateTime);
        using var source = fixture.Open();
        using var output = new MemoryStream();
        var indexed = precise.AddTicks(-7);
        _ = source.CopyVerifiedTo(output, fixture.Bytes.Length, indexed, 4096, CancellationToken.None);
        CollectionAssert.AreEqual(fixture.Bytes, output.ToArray());
        File.SetLastWriteTimeUtc(fixture.Asset, precise.AddTicks(1).UtcDateTime);
        Assert.ThrowsExactly<ReadOnlyWorkerException>(() => source.VerifyCurrent(CancellationToken.None));
    }

    [TestMethod]
    public void CancelledOpenAndCopyDoNotReadOrWriteContent()
    {
        using var fixture = new ImageSourceFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Open(cancellation.Token));
        using var source = fixture.Open();
        using var output = new MemoryStream();
        Assert.ThrowsExactly<OperationCanceledException>(() => source.CopyVerifiedTo(output,
            fixture.Bytes.Length, fixture.Modified, 4096, cancellation.Token));
        Assert.AreEqual(0, output.Length);
    }

    [TestMethod]
    public async Task ReadBoundaryRejectsDirectoryLinksAndPlainDirectories()
    {
        using var fixture = new ImageSourceFixture();
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "outside"));
        File.WriteAllBytes(Path.Combine(other.FullName, "linked.bin"), fixture.Bytes);
        await fixture.LinkDirectoryAsync(Path.Combine(fixture.Library, "alias"), other.FullName);
        Assert.ThrowsExactly<ReadOnlyWorkerException>(() => fixture.OpenRelative("alias/linked.bin"));
        Assert.ThrowsExactly<ReadOnlyWorkerException>(() => fixture.OpenRelative("中文"));
    }

    [TestMethod]
    public async Task LibraryRootDirectoryLinkIsRejected()
    {
        using var fixture = new ImageSourceFixture();
        var alias = Path.Combine(fixture.Root, "library-alias");
        await fixture.LinkDirectoryAsync(alias, fixture.Library);
        Assert.ThrowsExactly<ReadOnlyWorkerException>(() => StableImageSourceFile.Open(
            new CanonicalLibraryRoot(alias, fixture.Comparison), new RelativeAssetPath("中文/source.bin"), CancellationToken.None));
    }

    [TestMethod]
    public void LeafSymbolicLinkIsRejected()
    {
        using var fixture = new ImageSourceFixture();
        try
        {
            File.CreateSymbolicLink(Path.Combine(fixture.Library, "leaf.bin"), fixture.Asset);
        }
        catch (IOException exception) when (OperatingSystem.IsWindows() && (exception.HResult & 0xffff) == 1314)
        {
            Assert.Inconclusive("This Windows token cannot create a file symbolic link; Linux or a capable Windows runner must supply this evidence.");
        }

        Assert.ThrowsExactly<ReadOnlyWorkerException>(() => fixture.OpenRelative("leaf.bin"));
    }

    [TestMethod]
    public void ChangesAreDeniedOrDetectedUsingTheOpenFileIdentity()
    {
        using var fixture = new ImageSourceFixture();
        using var source = fixture.Open();
        if (OperatingSystem.IsWindows())
        {
            Assert.ThrowsExactly<IOException>(() => File.WriteAllText(fixture.Asset, "changed"));
            Assert.ThrowsExactly<IOException>(() => File.Move(fixture.Asset, fixture.Asset + ".moved"));
            source.VerifyCurrent(CancellationToken.None);
        }
        else
        {
            File.WriteAllText(fixture.Asset, "changed");
            Assert.ThrowsExactly<ReadOnlyWorkerException>(() => source.VerifyCurrent(CancellationToken.None));
        }
    }

    [TestMethod]
    public void WindowsAliasesAreRejectedAndPathDepthIsBounded()
    {
        using var fixture = new ImageSourceFixture();
        if (OperatingSystem.IsWindows())
        {
            foreach (var alias in new[] { "中文/source.bin:secret", "中文/source.bin ", "CON", "LPT¹.txt", "AUX.dat" })
            {
                Assert.ThrowsExactly<ReadOnlyWorkerException>(() => ImageSourcePath.Create(
                    fixture.CanonicalRoot, new RelativeAssetPath(alias)));
            }
        }

        Assert.ThrowsExactly<ReadOnlyWorkerException>(() => ImageSourcePath.Create(fixture.CanonicalRoot,
            new RelativeAssetPath(string.Join('/', Enumerable.Repeat("segment", 129)))));
    }

    [TestMethod]
    public void DisposalReleasesAllDirectoryAndFileHandles()
    {
        using var fixture = new ImageSourceFixture();
        var opened = fixture.Open();
        opened.Dispose();
        Directory.Move(fixture.Library, fixture.Library + "-moved");
        Assert.ThrowsExactly<ObjectDisposedException>(() => opened.VerifyCurrent(CancellationToken.None));
    }
}

internal sealed class ImageSourceFixture : IDisposable
{
    private readonly RepositorySandbox sandbox = new();
    public string Root => sandbox.Root;
    public string Library => Path.Combine(Root, "library");
    public string Asset => Path.Combine(Library, "中文", "source.bin");
    public byte[] Bytes { get; } = Enumerable.Range(0, 1024).Select(value => (byte)(value % 251)).ToArray();
    public DateTimeOffset Modified { get; }
    public RootPathComparison Comparison { get; } = OperatingSystem.IsWindows() ? RootPathComparison.CaseInsensitive : RootPathComparison.CaseSensitive;
    public CanonicalLibraryRoot CanonicalRoot => new(Library, Comparison);

    public ImageSourceFixture()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Asset)!);
        File.WriteAllBytes(Asset, Bytes);
        Modified = File.GetLastWriteTimeUtc(Asset);
    }

    public StableImageSourceFile Open(CancellationToken token = default) =>
        StableImageSourceFile.Open(CanonicalRoot, new RelativeAssetPath("中文/source.bin"), token);

    public StableImageSourceFile OpenRelative(string relative) =>
        StableImageSourceFile.Open(CanonicalRoot, new RelativeAssetPath(relative), CancellationToken.None);

    public Task LinkDirectoryAsync(string link, string target) => SandboxDirectoryLink.CreateAsync(sandbox, link, target);

    public void Dispose()
    {
        sandbox.Dispose();
        GC.SuppressFinalize(this);
    }
}
