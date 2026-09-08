using System.Text.Json;
using System.Text.Json.Serialization;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

public static class ImageSourceWorker
{
    public static int Run(Stream input, Stream output)
    {
        try
        {
            ImageSourceParentLifetime.Require();
            var headerBytes = new byte[ImageWorkerProtocol.HeaderBytes];
            input.ReadExactly(headerBytes);
            var header = ImageWorkerProtocol.ReadHeader(headerBytes);
            if (header.Status != (int)ImageWorkerStatus.Request || header.Length is <= 0 or > ReadOnlyWorkerProtocol.FrameLimit)
            {
                throw new ReadOnlyWorkerException("preview_source_request_invalid");
            }
            var payload = new byte[header.Length];
            input.ReadExactly(payload);
            var request = JsonSerializer.Deserialize(payload, ImageSourceJsonContext.Default.ImageSourceRequest)
                ?? throw new ReadOnlyWorkerException("preview_source_request_invalid");
            using var source = StableImageSourceFile.Open(new CanonicalLibraryRoot(request.Root, request.Comparison),
                new RelativeAssetPath(request.RelativePath), CancellationToken.None);
            using var snapshot = Snapshot();
            var hash = source.CopyVerifiedTo(snapshot, request.Length, request.ModifiedAt, ImageWorkerProtocol.MaximumSourceBytes, CancellationToken.None);
            output.Write(ImageWorkerProtocol.Header((int)ImageWorkerStatus.Ready, 0, checked((int)snapshot.Length), 32));
            output.Write(Convert.FromHexString(hash));
            output.Flush();
            var copied = false;
            while (true)
            {
                var command = input.ReadByte();
                if (command is -1 or 3) return 0;
                if (command == 1 && !copied)
                {
                    copied = true;
                    snapshot.Position = 0;
                    snapshot.CopyTo(output, 64 * 1024);
                }
                else if (command == 2)
                {
                    source.VerifyCurrent(CancellationToken.None);
                }
                else
                {
                    throw new ReadOnlyWorkerException("preview_source_request_invalid");
                }
                output.Write(ImageWorkerProtocol.Header((int)ImageWorkerStatus.Success, 0, 0));
                output.Flush();
            }
        }
        catch (ReadOnlyWorkerException failure)
        {
            var status = failure.Code == "preview_source_changed" ? ImageWorkerStatus.SourceChanged
                : failure.Code == "preview_source_limit" ? ImageWorkerStatus.Limit : ImageWorkerStatus.Unavailable;
            WriteFailure(output, status);
            return 1;
        }
        catch (Exception)
        {
            WriteFailure(output, ImageWorkerStatus.Unavailable);
            return 1;
        }
    }

    private static FileStream Snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), "assetlibrary-image-source-" + Guid.NewGuid().ToString("N"));
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose,
            BufferSize = 64 * 1024,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var stream = new FileStream(path, options);
        try
        {
            if (!OperatingSystem.IsWindows()) File.Delete(path);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void WriteFailure(Stream output, ImageWorkerStatus status)
    {
        try { output.Write(ImageWorkerProtocol.Header((int)status, 0, 0)); output.Flush(); }
        catch (IOException) { /* The parent closes the bounded pipe during cancellation. */ }
    }
}

internal sealed record ImageSourceRequest(string Root, RootPathComparison Comparison, string RelativePath, long Length, DateTimeOffset ModifiedAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ImageSourceRequest))]
internal sealed partial class ImageSourceJsonContext : JsonSerializerContext;
