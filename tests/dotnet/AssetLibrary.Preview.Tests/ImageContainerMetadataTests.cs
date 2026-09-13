using AssetLibrary.ImagePreview.Isolation;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class ImageContainerMetadataTests
{
    private const string Decoder = """
        Uid:	1655 1655 1655 1655
        Gid:	1655 1655 1655 1655
        CapEff:	0000000000000000
        CapPrm:	0000000000000000
        CapInh:	0000000000000000
        NoNewPrivs:	1
        """;

    [TestMethod]
    [DataRow("Uid:	1655 1655 1655 1655", "Uid:	1655 1655 1655 0")]
    [DataRow("Gid:	1655 1655 1655 1655", "Gid:	1655 1655 0 1655")]
    [DataRow("CapPrm:	0000000000000000", "CapPrm:	00000000000000e1")]
    [DataRow("CapEff:	0000000000000000", "CapEff:	0000000000000001")]
    [DataRow("CapInh:	0000000000000000", "CapInh:	0000000000000020")]
    [DataRow("NoNewPrivs:	1", "NoNewPrivs:	0")]
    public void EveryCredentialFieldIsRequired(string before, string after)
    {
        Assert.IsTrue(LinuxContainerStatus.Valid(Decoder, 1655, 0));
        Assert.IsFalse(LinuxContainerStatus.Valid(Decoder.Replace(before, after, StringComparison.Ordinal), 1655, 0));
    }

    [TestMethod]
    [DataRow("/", "/sys/fs/cgroup/memory", "/docker/owned", "/sys/fs/cgroup/memory/docker/owned")]
    [DataRow("/docker/owned", "/sys/fs/cgroup/memory", "/docker/owned", "/sys/fs/cgroup/memory")]
    [DataRow("/", "/sys/fs/cgroup", "/", "/sys/fs/cgroup")]
    [DataRow("/docker/owned", "/sys/fs/cgroup/memory", "/docker/other", null)]
    [DataRow("/", "/sys/fs/cgroup-foreign", "/", null)]
    [DataRow("/", "/sys/fs/cgroup", "/../other", null)]
    public void KernelMembershipMustResolveWithinItsActualControllerMount(string root, string mount, string member, string? expected) =>
        Assert.AreEqual(expected, LinuxContainerMemory.Resolve(root, mount, member));
}
