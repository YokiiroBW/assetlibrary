using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed record TrialOperatorInput(AdministratorOperatorRequest Request, LocalSecret Secret) : IDisposable
{
    private const int MaximumInputBytes = 16384;

    public static async ValueTask<TrialOperatorInput> ReadAsync(
        string action, Stream input, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaximumInputBytes + 1];
        try
        {
            var length = 0;
            while (length <= MaximumInputBytes)
            {
                var read = await input.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return ParseRequest(action, buffer.AsMemory(0, length));
                }

                length += read;
            }

            throw new ArgumentException("The operator input is too large.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static TrialOperatorInput ParseRequest(string action, ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The operator input is invalid.");
        }

        var expectedCount = action == "bootstrap" ? 6 : 5;
        if (value.EnumerateObject().Count() != expectedCount)
        {
            throw new JsonException("The operator input fields are invalid.");
        }

        var request = new AdministratorOperatorRequest(
            Guid.ParseExact(Required(value, "authorization_id"), "D"),
            Guid.ParseExact(Required(value, "operation_id"), "D"),
            action == "bootstrap" ? AdministratorBootstrapRecoveryAction.BootstrapFirstAdministrator
                : AdministratorBootstrapRecoveryAction.RecoverAdministrator,
            new LocalAccountName(Required(value, "account_name")),
            action == "bootstrap" ? new LocalAccountDisplayName(Required(value, "display_name")) : null,
            DateTimeOffset.Parse(Required(value, "expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        return new TrialOperatorInput(request, new LocalSecret(Required(value, "password")));
    }

    private static string Required(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("A required operator field is invalid.");
        }

        return property.GetString()!;
    }

    public void Dispose() => Secret.Dispose();

    public override string ToString() => "[redacted]";
}
