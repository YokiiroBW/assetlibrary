using System.Buffers;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

/// <summary>
/// Reads one bounded UTF-8 JSON request body for every AssetLink operation. It lives once because the
/// ceiling and the decode rules are a transport policy: a second copy would let one operation accept a
/// body size or an encoding that another refuses.
/// </summary>
public static class AssetLinkRequestBody
{
    public const int MaximumBytes = 64 * 1024;

    public static async ValueTask<string> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasJsonContentType())
        {
            throw new UnsupportedRequestMediaTypeException();
        }

        if (request.ContentLength > MaximumBytes)
        {
            throw new RequestBodyTooLargeException();
        }

        var writer = new ArrayBufferWriter<byte>();
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (writer.WrittenCount + read > MaximumBytes)
                {
                    throw new RequestBodyTooLargeException();
                }

                writer.Write(buffer.AsSpan(0, read));
            }

            // Strict decoding: a body that is not valid UTF-8 is refused rather than replaced, so a
            // mangled operation name is never silently executed as a different one.
            return new UTF8Encoding(false, true).GetString(writer.WrittenSpan);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

public sealed class RequestBodyTooLargeException : Exception;

public sealed class UnsupportedRequestMediaTypeException : Exception;
