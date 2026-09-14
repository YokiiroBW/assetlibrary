using System.Text.Json;
using AssetLibrary.AssetLink;
using AssetLibrary.CoreServer.Adapters.AssetLink;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class ServiceReadControl
{
    public static ValueTask<AssetLinkProtocolResponse> HandleAsync(HttpContext context, string payload, CancellationToken token)
    {
        try
        {
            if (AssetLinkCodec.Parse(payload) is not ControlRequestMessage request)
                throw new JsonException("Expected a control request.");
            if (request.Operation is "libraries.list" or "libraries.get" or "entries.browse" or "entries.get" or "assets.search")
                return context.RequestServices.GetRequiredService<ReadOnlyAssetLinkProtocol>().HandleAsync(context.User, payload, token);
            var requestId = !string.IsNullOrEmpty(request.RequestId) && request.RequestId.Length <= 128
                && !request.RequestId.Any(char.IsControl) ? request.RequestId : "unknown";
            return ValueTask.FromResult(ReadOnlyAssetLinkProtocol.ControlError(403, requestId,
                "permission_denied", "Service credentials only permit read operations."));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            return ValueTask.FromResult(ReadOnlyAssetLinkProtocol.TransportError(400, "invalid_request", "The request is invalid."));
        }
    }
}
