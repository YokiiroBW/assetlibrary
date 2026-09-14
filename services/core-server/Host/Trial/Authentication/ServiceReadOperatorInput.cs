using System.Text.Json;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal static class ServiceReadOperatorInput
{
    public static async ValueTask<ServiceReadOperatorRequest> ReadAsync(string action, Stream input, CancellationToken token)
    {
        var buffer = new byte[16385];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await input.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
            if (count == 0) break;
            length += count;
        }
        if (length > 16384) throw new ArgumentException("Operator input too large.");
        using var document = JsonDocument.Parse(buffer.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 4 });
        var value = document.RootElement;
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
        {
            if (!fields.Add(field.Name) || field.Name is not ("authorization_id" or "operation_id" or "principal_id"
                or "operator_id" or "expires_at" or "display_name" or "library_ids" or "credential_id" or "lifetime_days"))
                throw new JsonException("Invalid operator fields.");
        }
        var operation = action switch
        {
            "service-create" => ServiceReadOperatorAction.Create,
            "service-grant" => ServiceReadOperatorAction.Grant,
            "service-ungrant" => ServiceReadOperatorAction.Ungrant,
            "service-issue" => ServiceReadOperatorAction.Issue,
            "service-rotate" => ServiceReadOperatorAction.Rotate,
            "service-revoke" => ServiceReadOperatorAction.Revoke,
            "service-disable" => ServiceReadOperatorAction.Disable,
            _ => throw new ArgumentException("Invalid service action."),
        };
        return new ServiceReadOperatorRequest(value.GetProperty("authorization_id").GetGuid(),
            value.GetProperty("operation_id").GetGuid(), operation, value.GetProperty("principal_id").GetGuid(),
            value.GetProperty("operator_id").GetString()!, value.GetProperty("expires_at").GetDateTimeOffset(),
            value.TryGetProperty("display_name", out var display) ? display.GetString() : null,
            value.TryGetProperty("library_ids", out var libraries) ? libraries.EnumerateArray().Select(item => item.GetGuid()) : null,
            value.TryGetProperty("credential_id", out var credential) ? credential.GetGuid() : null,
            value.TryGetProperty("lifetime_days", out var lifetime) ? lifetime.GetInt32() : null);
    }
}
