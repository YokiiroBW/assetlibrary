using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal static class ThumbnailDecodeHelper
{
    internal static async Task<int> RunAsync()
    {
        try
        {
            if (!ThumbnailDecodeJob.CurrentIsBounded()) { Console.Error.WriteLine("thumbnail_decode:job_required"); return 2; }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            var png = await ThumbnailDecodeWire.ReadInputAsync(input, deadline.Token).ConfigureAwait(false);
            var pixels = WicThumbnailDecoder.Decode(png);
            await ThumbnailDecodeWire.WriteOutputAsync(output, pixels, deadline.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or OperationCanceledException or COMException)
        { Console.Error.WriteLine("thumbnail_decode:rejected"); return 2; }
    }
}
