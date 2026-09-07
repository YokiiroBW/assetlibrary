using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.Modules.GatewayAuth.Application;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

public sealed class ReadOnlyAssetLinkProtocol(
    ReadOnlyBrowseService browse,
    ILogger<ReadOnlyAssetLinkProtocol> logger)
{
    private readonly ReadOnlyBrowseService browse = browse ?? throw new ArgumentNullException(nameof(browse));
    private readonly ILogger<ReadOnlyAssetLinkProtocol> logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask<AssetLinkProtocolResponse> HandleAsync(
        ClaimsPrincipal principal,
        string payload,
        CancellationToken cancellationToken)
    {
        var requestId = "unknown";
        var operation = "unknown";
        try
        {
            var request = AssetLinkReadRequestParser.Parse(principal, payload);
            requestId = request.RequestId;
            operation = request.Operation;
            var response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            AssetLinkReadLog.Completed(logger, requestId, operation, response.StatusCode);
            return response;
        }
        catch (AssetLinkAuthenticationException)
        {
            return Reject(requestId, operation, 401, "authentication_required", "Authentication is required.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException)
        {
            return Reject(requestId, operation, 400, "invalid_request", "The request is invalid.");
        }
        catch (TimeoutException)
        {
            return Reject(requestId, operation, 504, "timeout", "The read request timed out.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Reject(
                requestId,
                operation,
                503,
                "service_unavailable",
                "The read service is unavailable.");
        }
    }

    public static AssetLinkProtocolResponse TransportError(int statusCode, string code, string message) =>
        AssetLinkReadJson.Error(statusCode, "unknown", code, message);

    public static AssetLinkProtocolResponse ControlResult(string requestId, JsonObject body) =>
        AssetLinkReadJson.Success(requestId, body);

    public static AssetLinkProtocolResponse ControlError(int statusCode, string requestId, string code, string message) =>
        AssetLinkReadJson.Error(statusCode, requestId, code, message);

    private async ValueTask<AssetLinkProtocolResponse> ExecuteAsync(
        AssetLinkReadRequest request,
        CancellationToken cancellationToken)
    {
        switch (request)
        {
            case ListLibrariesAssetLinkRequest libraries:
                return AssetLinkReadJson.Success(
                    request.RequestId,
                    AssetLinkReadJson.Libraries(await browse.ListLibrariesAsync(
                        libraries.Query,
                        cancellationToken).ConfigureAwait(false)));
            case BrowseEntriesAssetLinkRequest entries:
                var page = await browse.BrowseEntriesAsync(entries.Query, cancellationToken)
                    .ConfigureAwait(false);
                return page is null
                    ? AssetLinkReadJson.Error(
                        404,
                        request.RequestId,
                        "not_found",
                        "The requested resource is not available.")
                    : AssetLinkReadJson.Success(request.RequestId, AssetLinkReadJson.Entries(page));
            case SearchAssetsAssetLinkRequest search:
                return AssetLinkReadJson.Success(
                    request.RequestId,
                    AssetLinkReadJson.Search(await browse.SearchAssetsAsync(
                        search.Query,
                        cancellationToken).ConfigureAwait(false)));
            case UnsupportedAssetLinkReadRequest:
                return AssetLinkReadJson.Error(
                    400,
                    request.RequestId,
                    "unsupported_operation",
                    "The requested operation is not available in the read-only API.");
            default:
                throw new InvalidOperationException("An unknown read request reached the protocol adapter.");
        }
    }

    private AssetLinkProtocolResponse Reject(
        string requestId,
        string operation,
        int statusCode,
        string code,
        string message)
    {
        AssetLinkReadLog.Rejected(logger, requestId, operation, code);
        return AssetLinkReadJson.Error(statusCode, requestId, code, message);
    }
}
