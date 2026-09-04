using System.Buffers;
using System.Security.Cryptography;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PwnedPasswordsResponseReader
{
    public static async Task<byte[]?> ReadAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(maximumBytes + 1);
        try
        {
            var total = 0;
            while (total <= maximumBytes)
            {
                var read = await stream.ReadAsync(
                    rented.AsMemory(total, maximumBytes + 1 - total),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return rented.AsSpan(0, total).ToArray();
                }

                total += read;
            }

            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
