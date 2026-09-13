using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.ImageSupervisor;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class ImageContainerProtocolTests
{
    [TestMethod]
    public void FixedSourceHeaderAndExactCopyPreserveTheDisconnectMonitorByte()
    {
        var request = ImageWorkerProtocol.ReadHeader(Convert.FromHexString("415049310200000001000000030000000000000000000000"));
        LocalImageFrames.RequireRequest(request);
        Assert.AreEqual(1, request.Profile); Assert.AreEqual(3, request.Length);
    }

    [TestMethod]
    public async Task RelayCopiesOnlyTheDeclaredSourceAndRejectsTruncation()
    {
        using var source = new MemoryStream(new byte[] { 1, 2, 3, 99 }); using var destination = new MemoryStream();
        await LocalImageFrames.CopyExactlyAsync(source, destination, 3, CancellationToken.None);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, destination.ToArray()); Assert.AreEqual(99, source.ReadByte());
        using var truncated = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsExactlyAsync<ImageClientRejected>(() => LocalImageFrames.CopyExactlyAsync(truncated, Stream.Null, 2, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(2, 0, 0, 0, 0)]
    [DataRow(2, 0, 33554433, 0, 0)]
    [DataRow(2, 2, 1, 0, 0)]
    [DataRow(2, 1, 1, 1, 0)]
    [DataRow(1, 0, 1, 0, 0)]
    public void InvalidRequestsNeverReachAChild(int status, int profile, int length, int width, int height) =>
        Assert.ThrowsExactly<InvalidDataException>(() => LocalImageFrames.RequireRequest(new(status, profile, length, width, height)));

    [TestMethod]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public void FailureFramesCannotCarryPixelsOrProfile(int status)
    {
        Assert.IsTrue(LocalImageFrames.IsImageFailure(new(status, 0, 0, 0, 0)));
        Assert.IsFalse(LocalImageFrames.IsImageFailure(new(status, 1, 0, 0, 0)));
        Assert.IsFalse(LocalImageFrames.IsImageFailure(new(status, 0, 1, 0, 0)));
    }

    [TestMethod]
    public void SuccessCannotExceedEitherClosedProfile()
    {
        LocalImageFrames.RequireOutput(new(3, 1, 12582912, 1600, 1600), 1);
        Assert.ThrowsExactly<InvalidDataException>(() => LocalImageFrames.RequireOutput(new(3, 1, 12582913, 1600, 1600), 1));
        Assert.ThrowsExactly<InvalidDataException>(() => LocalImageFrames.RequireOutput(new(3, 0, 2097152, 513, 512), 0));
        Assert.ThrowsExactly<InvalidDataException>(() => LocalImageFrames.RequireOutput(new(3, 0, 33, 1, 1), 1));
    }
}
