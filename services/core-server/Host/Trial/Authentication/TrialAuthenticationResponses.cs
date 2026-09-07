using System.Text;
using System.Text.Json.Serialization;
using AssetLibrary.CoreServer.Adapters.AssetLink;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal static class TrialAuthenticationResponses
{
    public static IResult Error(int statusCode, string code, string message, HttpContext? context = null)
    {
        if (context?.Request.Path == "/assetlink/v1/control")
        {
            var response = ReadOnlyAssetLinkProtocol.TransportError(statusCode, code, message);
            return Results.Text(response.Json, "application/json", Encoding.UTF8, response.StatusCode);
        }

        return Results.Json(new AuthenticationError(code, message), statusCode: statusCode);
    }

    public static IResult AuthenticationRequired(HttpContext? context = null) =>
        Error(401, "authentication_required", "请登录后继续。", context);

    public static IResult Forbidden(HttpContext? context = null) =>
        Error(403, "forbidden", "请求未通过安全验证。", context);

    public static IResult Unavailable(HttpContext? context = null) =>
        Error(503, "service_unavailable", "认证服务暂时不可用，请稍后重试。", context);

    public static IResult Session(AuthenticatedIdentity identity, TrialBrowserTicket ticket) =>
        Results.Json(new SessionPayload(
            true, identity.PrincipalId.ToString("D"), identity.DisplayName,
            identity.IsSystemAdministrator, ticket.CsrfToken.Export(), ticket.AbsoluteExpiresAt));

    public static void PreventCaching(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers.Vary = "Cookie";
    }

    private sealed record AuthenticationError(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")] string Message);

    private sealed record SessionPayload(
        [property: JsonPropertyName("authenticated")] bool Authenticated,
        [property: JsonPropertyName("principal_id")] string PrincipalId,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("is_system_administrator")] bool IsSystemAdministrator,
        [property: JsonPropertyName("csrf_token")] string CsrfToken,
        [property: JsonPropertyName("absolute_expires_at")] DateTimeOffset AbsoluteExpiresAt);
}
