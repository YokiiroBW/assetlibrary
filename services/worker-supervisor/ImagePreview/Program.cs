using System.Runtime.CompilerServices;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.ImagePreview.Worker;
using Microsoft.Win32.SafeHandles;

return ImageWorkerEntry.Run(args);

internal static class ImageWorkerEntry
{
    public static int Run(string[] arguments)
    {
        var probeMode = arguments.Length == 1 && OperatingSystem.IsLinux()
            && arguments[0] is "--probe-isolation" or "--probe-memory" or "--probe-cpu" or "--probe-threads";
        if (arguments.Length != 0 && !probeMode) return 2;
        // Unix Console streams duplicate their descriptors and lazily initialize Console.Out.
        // The sandbox deliberately allows only the original fd 0/1, never arbitrary dup/open.
        using var input = OperatingSystem.IsLinux()
            ? new FileStream(new SafeFileHandle(0, ownsHandle: false), FileAccess.Read, 1, isAsync: false)
            : Console.OpenStandardInput();
        using var output = OperatingSystem.IsLinux()
            ? new FileStream(new SafeFileHandle(1, ownsHandle: false), FileAccess.Write, 1, isAsync: false)
            : Console.OpenStandardOutput();
        try
        {
            using var probe = probeMode && OperatingSystem.IsLinux() ? LinuxImageProbe.Prepare(arguments[0]) : null;
            // No host configuration, environment credentials or original paths are read here.
            Warmup();
            var confined = OperatingSystem.IsLinux() ? LinuxImageIsolation.Enter()
                : OperatingSystem.IsWindows() && WindowsImageIsolation.IsEnforced();
            if (!confined) throw new ImageDecodeException(ImageWorkerStatus.Unavailable);
            if (OperatingSystem.IsLinux() && probe is not null) return probe.Run(output);
            output.Write(ImageWorkerProtocol.Header((int)ImageWorkerStatus.Ready, 0, 0));
            output.Flush();
            var headerBytes = new byte[ImageWorkerProtocol.HeaderBytes];
            input.ReadExactly(headerBytes);
            var request = ImageWorkerProtocol.ReadHeader(headerBytes);
            if (request.Status != (int)ImageWorkerStatus.Request || request.Length is <= 0 or > ImageWorkerProtocol.MaximumSourceBytes
                || request.Width != 0 || request.Height != 0)
            {
                throw new ImageDecodeException(ImageWorkerStatus.Invalid);
            }

            _ = ImageWorkerProtocol.MaximumEdge(request.Profile);
            var source = new byte[request.Length];
            input.ReadExactly(source);
            if (input.ReadByte() != -1) throw new ImageDecodeException(ImageWorkerStatus.Invalid);
            var result = StaticImageDecoder.Decode(source, request.Profile);
            output.Write(ImageWorkerProtocol.Header((int)ImageWorkerStatus.Success, request.Profile,
                result.Png.Length, result.Width, result.Height));
            output.Write(result.Png);
            output.Flush();
            return 0;
        }
        catch (ImageDecodeException failure)
        {
            WriteFailure(output, failure.Status);
            return 1;
        }
        catch (OutOfMemoryException)
        {
            WriteFailure(output, ImageWorkerStatus.Limit);
            return 1;
        }
        catch (Exception)
        {
            // Decoder paths and native diagnostics never cross this bounded protocol.
            WriteFailure(output, ImageWorkerStatus.Unavailable);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Warmup()
    {
        using var pixel = new SkiaSharp.SKBitmap(1, 1);
        pixel.Erase(SkiaSharp.SKColors.Transparent);
        using var image = SkiaSharp.SKImage.FromBitmap(pixel);
        foreach (var format in new[] { SkiaSharp.SKEncodedImageFormat.Png, SkiaSharp.SKEncodedImageFormat.Jpeg, SkiaSharp.SKEncodedImageFormat.Webp })
        {
            using var bytes = image.Encode(format, 90);
            _ = StaticImageDecoder.Decode(bytes.ToArray(), 0);
        }

        _ = Environment.ProcessorCount;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static void WriteFailure(Stream output, ImageWorkerStatus status)
    {
        try
        {
            output.Write(ImageWorkerProtocol.Header((int)status, 0, 0));
            output.Flush();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // The parent can close the pipe as part of cancellation; the worker still terminates.
        }
    }
}
