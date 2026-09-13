using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal static class ThumbnailDecodeHelper
{
    internal static Task<int> RunAsync() => RunForAsync(DerivedImageProfile.Thumbnail512);
    internal static Task<int> RunPreviewAsync() => RunForAsync(DerivedImageProfile.Preview1600);
    private static async Task<int> RunForAsync(DerivedImageProfile profile)
    {
        try
        {
            if (!ThumbnailDecodeJob.CurrentIsBounded()) { Console.Error.WriteLine("thumbnail_decode:job_required"); return 2; }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            var png = await ThumbnailDecodeWire.ReadInputAsync(input, deadline.Token, profile).ConfigureAwait(false);
            var pixels = WicThumbnailDecoder.Decode(png, profile);
            await ThumbnailDecodeWire.WriteOutputAsync(output, pixels, deadline.Token, profile).ConfigureAwait(false);
            return 0;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or OperationCanceledException or COMException)
        { Console.Error.WriteLine("thumbnail_decode:rejected"); return 2; }
    }
}
