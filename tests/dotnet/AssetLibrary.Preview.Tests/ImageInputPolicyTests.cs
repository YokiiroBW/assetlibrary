using System.Buffers.Binary;
using System.Text;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.ImagePreview.Worker;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class ImageInputPolicyTests
{
    [TestMethod]
    [DataRow("vpAg", 4194304, 0x0CCB8FD8u, true)]
    [DataRow("vpAg", 4194305, 0x5A067EE4u, false)]
    [DataRow("tEXt", 4194305, 0x4CEA9AC2u, false)]
    [DataRow("IDAT", 8388608, 0xAAE738E6u, true)]
    public void NonIdatCopyBudgetIsIndependentOfTheWholeSourceBudget(string tag, int length, uint crc, bool allowed)
    {
        var png = TestPng.Create(1, 1);
        var chunk = new byte[length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk, length);
        Encoding.ASCII.GetBytes(tag).CopyTo(chunk, 4);
        // Independent zlib-generated CRC constants: the precheck is exercised without invoking a native decoder.
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(length + 8), crc);
        var input = new byte[png.Length + chunk.Length];
        png.AsSpan(0, 33).CopyTo(input); chunk.CopyTo(input, 33); png.AsSpan(33).CopyTo(input.AsSpan(33 + chunk.Length));
        Assert.IsLessThanOrEqualTo(ImageWorkerProtocol.MaximumSourceBytes, input.Length);
        if (allowed) ImageInputPolicy.Validate(input);
        else Assert.AreEqual(ImageWorkerStatus.Limit, Assert.ThrowsExactly<ImageDecodeException>(() => ImageInputPolicy.Validate(input)).Status);
    }
}
