using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.ReadCore.Tests;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class WindowsImageProcessTests
{
    [TestMethod]
    public async Task NativeAotDecoderRunsInsideLpacAndReturnsARealThumbnail()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This test requires the Windows LPAC launcher.");
            return;
        }
        var executable = Environment.GetEnvironmentVariable("ASSETLIBRARY_IMAGE_WORKER_TEST_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable))
        {
            Assert.Inconclusive("The platform runner must provide the actual published NativeAOT decoder executable.");
            return;
        }
        using var sandbox = new RepositorySandbox();
        var profiles = Path.Combine(sandbox.Root, "profiles");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using (var worker = Start(executable, profiles, deadline.Token))
        {
            var readyBytes = new byte[ImageWorkerProtocol.HeaderBytes];
            try
            {
                await worker.Output.ReadExactlyAsync(readyBytes, deadline.Token);
            }
            catch (EndOfStreamException)
            {
                await worker.Process.WaitForExitAsync(deadline.Token);
                var diagnostics = new byte[4097];
                var count = await worker.Error.ReadAtLeastAsync(diagnostics, diagnostics.Length, throwOnEndOfStream: false, deadline.Token);
                Assert.IsLessThanOrEqualTo(4096, count);
                Assert.Fail($"Decoder exited before readiness: 0x{worker.ExitCode:X8}; {SafeDiagnostics(diagnostics, count)}");
            }
            var ready = ImageWorkerProtocol.ReadHeader(readyBytes);
            if (ready.Status != (int)ImageWorkerStatus.Ready)
            {
                await worker.Process.WaitForExitAsync(deadline.Token);
                var diagnostics = new byte[4096];
                var count = await worker.Error.ReadAsync(diagnostics, deadline.Token);
                Assert.Fail($"Native startup status {ready.Status}; exit {worker.ExitCode}; stages {SafeDiagnostics(diagnostics, count)}.");
            }
            Assert.AreEqual((int)ImageWorkerStatus.Ready, ready.Status);
            var source = TestPng.Create(1024, 600);
            await worker.Input.WriteAsync(ImageWorkerProtocol.Header((int)ImageWorkerStatus.Request, 0, source.Length), deadline.Token);
            await worker.Input.WriteAsync(source, deadline.Token);
            worker.Input.Dispose();
            var resultBytes = new byte[ImageWorkerProtocol.HeaderBytes];
            await worker.Output.ReadExactlyAsync(resultBytes, deadline.Token);
            var result = ImageWorkerProtocol.ReadHeader(resultBytes);
            Assert.AreEqual((int)ImageWorkerStatus.Success, result.Status);
            Assert.AreEqual(512, result.Width);
            Assert.AreEqual(300, result.Height);
            Assert.IsTrue(result.Length > 33 && result.Length <= ImageWorkerProtocol.ThumbnailBytes);
            var png = new byte[result.Length];
            await worker.Output.ReadExactlyAsync(png, deadline.Token);
            Assert.IsTrue(png.AsSpan().StartsWith(ImageWorkerProtocol.PngSignature));
            Assert.AreEqual(512, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
            Assert.AreEqual(300, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
            Assert.AreEqual(8, png[24]);
            await worker.Process.WaitForExitAsync(deadline.Token);
            Assert.AreEqual(0, worker.ExitCode);
        }
        Assert.IsEmpty(Directory.GetFiles(profiles));
    }

    private static string SafeDiagnostics(byte[] diagnostics, int count)
    {
        // Only task-owned numeric stages and exception type/HRESULT may reach test output.
        var lines = Encoding.UTF8.GetString(diagnostics, 0, count).Split('\n')
            .Select(line => line.Trim()).Where(line => line.Length <= 160
                && (line.StartsWith("stage=", StringComparison.Ordinal)
                    || line.StartsWith("check=", StringComparison.Ordinal)
                    || line.StartsWith("exceptionType=", StringComparison.Ordinal))
                && line.All(character => char.IsAsciiLetterOrDigit(character) || character is ';' or '=' or '_'));
        return string.Join(" | ", lines);
    }

    [SupportedOSPlatform("windows")]
    private static WindowsImageProcess Start(string executable, string profiles, CancellationToken token)
    {
        try
        {
            return WindowsImageProcess.Start(executable, profiles, token);
        }
        catch (ReadOnlyWorkerException failure)
        {
            throw new AssertFailedException($"Native isolation startup failed: {failure.Code}, OS error {failure.NativeError}.");
        }
    }
}

internal static class TestPng
{
    public static byte[] Create(int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(ImageWorkerProtocol.PngSignature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[width * 4 + 1];
            for (var x = 0; x < width; x++)
            {
                row[x * 4 + 1] = 180;
                row[x * 4 + 3] = 80;
                row[x * 4 + 4] = 160;
            }
            for (var y = 0; y < height; y++) zlib.Write(row);
        }
        Chunk(output, "IDAT"u8, compressed.ToArray());
        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> bytes)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length);
        output.Write(number);
        output.Write(kind);
        output.Write(bytes);
        uint crc = uint.MaxValue;
        foreach (var value in kind) crc = AddByte(crc, value);
        foreach (var value in bytes) crc = AddByte(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint AddByte(uint checksum, byte value)
    {
        checksum ^= value;
        for (var bit = 0; bit < 8; bit++) checksum = (checksum >> 1) ^ ((checksum & 1) == 0 ? 0 : 0xedb88320);
        return checksum;
    }
}
