using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.AssetLink;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using static AssetLibrary.CoreServer.Adapters.AssetLink.AssetLinkReadFields;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

internal static class AssetLinkReadRequestParser
{
    private const int MaximumRequestIdLength = 128;

    public static AssetLinkReadRequest Parse(ClaimsPrincipal principal, string payload)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(payload);
        var subject = Subject(principal);
        var message = AssetLinkCodec.Parse(payload);
        if (message is not ControlRequestMessage request)
        {
            throw new JsonException("A control request is required.");
        }

        var requestId = RequestId(request.RequestId);
        if (request.IdempotencyKey is not null || request.CancelOf is not null)
        {
            throw new ArgumentException("Read-only controls cannot carry write or cancellation identity.");
        }

        var page = Page(request.Body, request.TimeoutMs);
        return AssetLinkReadBodyParser.Parse(requestId, request.Operation, subject, request.Body, page);
    }

    private static AuthenticatedSubject Subject(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new AssetLinkAuthenticationException();
        }

        var candidates = principal.Claims
            .Where(claim => claim.Type is ClaimTypes.NameIdentifier or "sub")
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new AssetLinkAuthenticationException();
        }

        try
        {
            return new AuthenticatedSubject(candidates[0]);
        }
        catch (ArgumentException)
        {
            throw new AssetLinkAuthenticationException();
        }
    }

    private static ReadPageOptions Page(JsonObject body, int? timeoutMilliseconds)
    {
        var pageSize = OptionalInteger(body, "page_size") ?? ReadPageOptions.DefaultPageSize;
        var cursorValue = OptionalString(body, "cursor");
        ReadPageCursor? cursor = cursorValue is null ? null : new ReadPageCursor(cursorValue);
        var timeout = timeoutMilliseconds is null
            ? ReadPageOptions.DefaultTimeout
            : TimeSpan.FromMilliseconds(timeoutMilliseconds.Value);
        return new ReadPageOptions(pageSize, timeout, cursor);
    }

    private static string RequestId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumRequestIdLength
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("The request ID is invalid.", nameof(value));
        }

        return value;
    }

}
