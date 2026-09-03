using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Domain;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class AssetIdentityAndPolicyTests
{
    [TestMethod]
    public void RelativePathNormalizesSeparatorsAndKeepsDisplayNameSeparateFromIdentity()
    {
        var path = new RelativeAssetPath("photos\\trip\\IMG_0001.JPG");

        Assert.AreEqual("photos/trip/IMG_0001.JPG", path.Value);
        Assert.AreEqual("IMG_0001.JPG", path.Name);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("/absolute/file.jpg")]
    [DataRow("C:/absolute/file.jpg")]
    [DataRow("folder/../file.jpg")]
    [DataRow("folder//file.jpg")]
    [DataRow("./file.jpg")]
    public void RelativePathRejectsEmptyAbsoluteTraversalAndAmbiguousSegments(string input)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelativeAssetPath(input));
    }

    [TestMethod]
    public void ObservationRequiresKindAppropriateLength()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.ThrowsExactly<ArgumentException>(() => new AssetObservation(
            StableEntryId.New(),
            new RelativeAssetPath("folder"),
            AssetEntryKind.Directory,
            1,
            now).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => new AssetObservation(
            StableEntryId.New(),
            new RelativeAssetPath("file.bin"),
            AssetEntryKind.File,
            null,
            now).Validate());

        var reparseFile = new AssetObservation(
            StableEntryId.New(),
            new RelativeAssetPath("linked-file"),
            AssetEntryKind.ReparseFile,
            null,
            now).Validate();
        Assert.IsNull(reparseFile.ContentLength);
    }

    [TestMethod]
    public void ObservationRejectsDefaultValueObjectIdentities()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new AssetObservation(
            default,
            new RelativeAssetPath("file.bin"),
            AssetEntryKind.File,
            1,
            DateTimeOffset.UtcNow).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => new AssetObservation(
            StableEntryId.New(),
            default,
            AssetEntryKind.File,
            1,
            DateTimeOffset.UtcNow).Validate());
    }

    [TestMethod]
    public void DefaultPolicyExcludesSystemAndTemporaryItems()
    {
        var time = DateTimeOffset.UtcNow;
        var ignoredDirectory = Entry(".assetmeta", AssetEntryKind.Directory, null, time);
        var ignoredFile = Entry("work.partial", AssetEntryKind.File, 1, time);
        var systemFile = Entry("system.dat", AssetEntryKind.File, 1, time, DiscoveryAttributes.System);
        var asset = Entry("photos/image.jpg", AssetEntryKind.File, 12, time);

        Assert.IsFalse(DefaultDiscoveryPolicy.ShouldInclude(ignoredDirectory));
        Assert.IsFalse(DefaultDiscoveryPolicy.ShouldRecurse(ignoredDirectory));
        Assert.IsFalse(DefaultDiscoveryPolicy.ShouldInclude(ignoredFile));
        Assert.IsFalse(DefaultDiscoveryPolicy.ShouldInclude(systemFile));
        Assert.IsTrue(DefaultDiscoveryPolicy.ShouldInclude(asset));
    }

    [TestMethod]
    public void ReparseDirectoryIsRecordedButNeverRecursed()
    {
        var entry = Entry(
            "linked-library",
            AssetEntryKind.ReparseDirectory,
            null,
            DateTimeOffset.UtcNow,
            DiscoveryAttributes.ReparsePoint);

        Assert.IsTrue(DefaultDiscoveryPolicy.ShouldInclude(entry));
        Assert.IsFalse(DefaultDiscoveryPolicy.ShouldRecurse(entry));
    }

    private static DiscoveredEntry Entry(
        string path,
        AssetEntryKind kind,
        long? length,
        DateTimeOffset modified,
        DiscoveryAttributes attributes = DiscoveryAttributes.None) =>
        new(new RelativeAssetPath(path), kind, length, modified, attributes);
}
