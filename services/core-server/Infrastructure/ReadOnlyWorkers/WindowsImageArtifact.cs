using System.Security.Cryptography;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal static class WindowsImageArtifact
{
    public static void Copy(string source, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > 64 * 1024 * 1024) throw new ReadOnlyWorkerException("preview_artifact_size_invalid");
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            digest.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
        }
        output.Flush(flushToDisk: true);
        output.Position = 0;
        using var copied = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while ((read = output.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            copied.AppendData(buffer, 0, read);
        }
        if (!CryptographicOperations.FixedTimeEquals(digest.GetHashAndReset(), copied.GetHashAndReset()))
            throw new ReadOnlyWorkerException("preview_artifact_copy_mismatch");
    }

}
