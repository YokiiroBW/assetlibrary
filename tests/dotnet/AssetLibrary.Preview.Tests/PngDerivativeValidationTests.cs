using AssetLibrary.Modules.PreviewProvider.Infrastructure;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class PngDerivativeValidationTests
{
    [TestMethod]
    public void RealSkiaOutputAllowsOnlyExactEightBitSignificanceBeforePixels()
    {
        var png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "skia-rgba8-sbit.png"));
        Assert.AreEqual("F67FE31590B54D36A47870FBDDDF748BBBDB4C4155196AAB25568254F8443057", Convert.ToHexString(SHA256.HashData(png)));
        Assert.IsTrue(PngDerivativeValidator.Valid(png));
        var wrongBits = (byte[])png.Clone();
        wrongBits[41] = 7;
        BinaryPrimitives.WriteUInt32BigEndian(wrongBits.AsSpan(45), 0x246b74de);
        Assert.IsFalse(PngDerivativeValidator.Valid(wrongBits));
        Assert.IsFalse(PngDerivativeValidator.Valid([.. png.AsSpan(0, 49), .. png.AsSpan(33, 16), .. png.AsSpan(49)]));
        Assert.IsFalse(PngDerivativeValidator.Valid([.. png.AsSpan(0, png.Length - 12), .. png.AsSpan(33, 16), .. png.AsSpan(png.Length - 12)]));
    }

    [TestMethod]
    public void CompletePngRequiresValidCrcAndTerminalIendAtEof()
    {
        var png = TestPng.Create(4, 3);
        Assert.IsTrue(PngDerivativeValidator.Valid(png));
        Assert.IsFalse(PngDerivativeValidator.Valid(png.AsSpan(0, png.Length - 1)));
        Assert.IsFalse(PngDerivativeValidator.Valid([.. png, 0]));
        Assert.IsFalse(PngDerivativeValidator.Valid(png.AsSpan(0, png.Length - 12)));
        var badCrc = (byte[])png.Clone();
        badCrc[29] ^= 1;
        Assert.IsFalse(PngDerivativeValidator.Valid(badCrc));
        Assert.IsFalse(PngDerivativeValidator.Valid("<svg onload='alert(1)'/>"u8));
    }

    [TestMethod]
    public void EvenValidCrcCannotAdmitTextAnimationOrRepeatedHeaderChunks()
    {
        var png = TestPng.Create(4, 3);
        // Independent PNG CRC values for a zero-length tEXt and eight-zero-byte acTL fixture.
        byte[] text = [0, 0, 0, 0, 116, 69, 88, 116, 0x96, 0x42, 0xc5, 0x85];
        byte[] animation = [0, 0, 0, 8, 97, 99, 84, 76, 0, 0, 0, 0, 0, 0, 0, 0, 0x89, 0x4d, 0xc0, 0x10];
        Assert.IsFalse(PngDerivativeValidator.Valid([.. png.AsSpan(0, 33), .. text, .. png.AsSpan(33)]));
        Assert.IsFalse(PngDerivativeValidator.Valid([.. png.AsSpan(0, 33), .. animation, .. png.AsSpan(33)]));
        Assert.IsFalse(PngDerivativeValidator.Valid([.. png.AsSpan(0, 33), .. png.AsSpan(8, 25), .. png.AsSpan(33)]));
    }
}
