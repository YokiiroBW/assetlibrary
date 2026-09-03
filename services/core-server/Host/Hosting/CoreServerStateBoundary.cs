namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerStateBoundary
{
    public static async Task<bool> IsWritableAsync(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        var probePath = Path.Combine(
            statePath,
            $".assetlibrary-host-write-probe-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            await using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await probe.WriteAsync(new byte[] { 0x41 }).ConfigureAwait(false);
            await probe.FlushAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
