using AssetLibrary.ImagePreview.Protocol;
using SkiaSharp;

namespace AssetLibrary.ImagePreview.Worker;

internal sealed record DecodedImage(byte[] Png, int Width, int Height);

internal static class StaticImageDecoder
{
    public static DecodedImage Decode(byte[] source, int profile)
    {
        ImageInputPolicy.Validate(source);
        using var data = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(data) ?? throw new ImageDecodeException(ImageWorkerStatus.Invalid);
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp)
            || codec.FrameCount > 1)
        {
            throw new ImageDecodeException(ImageWorkerStatus.Unsupported);
        }

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0) throw new ImageDecodeException(ImageWorkerStatus.Invalid);
        ImageInputPolicy.ValidateDimensions(checked((ulong)info.Width), checked((ulong)info.Height));
        using var srgb = SKColorSpace.CreateSrgb();
        var rgba = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb);
        using var bitmap = new SKBitmap();
        if (!bitmap.TryAllocPixels(rgba)) throw new ImageDecodeException(ImageWorkerStatus.Limit);
        if (codec.GetPixels(rgba, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            throw new ImageDecodeException(ImageWorkerStatus.Invalid);
        }

        return ResizeAndEncode(bitmap, codec.EncodedOrigin, profile, srgb);
    }

    private static DecodedImage ResizeAndEncode(SKBitmap bitmap, SKEncodedOrigin origin, int profile, SKColorSpace srgb)
    {
        var rotated = (int)origin >= 5;
        var width = rotated ? bitmap.Height : bitmap.Width;
        var height = rotated ? bitmap.Width : bitmap.Height;
        var scale = Math.Min(1d, (double)ImageWorkerProtocol.MaximumEdge(profile) / Math.Max(width, height));
        var targetWidth = Math.Max(1, (int)Math.Floor(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Floor(height * scale));
        using var target = new SKBitmap();
        if (!target.TryAllocPixels(new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul, srgb)))
        {
            throw new ImageDecodeException(ImageWorkerStatus.Limit);
        }
        using (var canvas = new SKCanvas(target))
        using (var image = SKImage.FromBitmap(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)targetWidth / width, (float)targetHeight / height);
            canvas.Concat(Orientation(origin, bitmap.Width, bitmap.Height));
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            canvas.Flush();
        }

        using var rendered = SKImage.FromBitmap(target);
        using var encoded = rendered.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new ImageDecodeException(ImageWorkerStatus.Limit);
        if (encoded.Size > ImageWorkerProtocol.MaximumOutput(profile)) throw new ImageDecodeException(ImageWorkerStatus.Limit);
        return new DecodedImage(encoded.ToArray(), targetWidth, targetHeight);
    }

    private static SKMatrix Orientation(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopLeft => SKMatrix.Identity,
        SKEncodedOrigin.TopRight => Matrix(-1, 0, width, 0, 1, 0),
        SKEncodedOrigin.BottomRight => Matrix(-1, 0, width, 0, -1, height),
        SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, height),
        SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
        SKEncodedOrigin.RightTop => Matrix(0, -1, height, 1, 0, 0),
        SKEncodedOrigin.RightBottom => Matrix(0, -1, height, -1, 0, width),
        SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, width),
        _ => throw new ImageDecodeException(ImageWorkerStatus.Unsupported),
    };

    private static SKMatrix Matrix(float a, float b, float x, float c, float d, float y) =>
        new(a, b, x, c, d, y, 0, 0, 1);
}
