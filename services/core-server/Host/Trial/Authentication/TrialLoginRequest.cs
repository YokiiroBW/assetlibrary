using System.Security.Cryptography;
using System.Text.Json;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed record TrialLoginRequest(LocalAccountName AccountName, LocalSecret Secret) : IDisposable
{
    private const int MaximumBodyBytes = 8192;

    public static async ValueTask<TrialLoginRequest> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            throw new TrialLoginRequestException(415);
        }

        if (request.ContentLength > MaximumBodyBytes)
        {
            throw new TrialLoginRequestException(413);
        }

        var buffer = new byte[MaximumBodyBytes + 1];
        try
        {
            var length = 0;
            while (length <= MaximumBodyBytes)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return Parse(buffer.AsMemory(0, length));
                }

                length += read;
            }

            throw new TrialLoginRequestException(413);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static TrialLoginRequest Parse(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var body = document.RootElement;
        if (body.ValueKind != JsonValueKind.Object
            || body.EnumerateObject().Count() != 2
            || !body.TryGetProperty("account_name", out var accountName) || accountName.ValueKind != JsonValueKind.String
            || !body.TryGetProperty("password", out var password) || password.ValueKind != JsonValueKind.String)
        {
            throw new TrialLoginRequestException(400);
        }

        return new TrialLoginRequest(new LocalAccountName(accountName.GetString()!), new LocalSecret(password.GetString()));
    }

    public void Dispose() => Secret.Dispose();

    public override string ToString() => "[redacted]";
}

internal sealed class TrialLoginRequestException(int statusCode) : ArgumentException("The login request is invalid.")
{
    public int StatusCode { get; } = statusCode;
}
