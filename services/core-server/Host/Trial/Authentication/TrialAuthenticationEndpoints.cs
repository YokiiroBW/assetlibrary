using System.Globalization;
using System.Text.Json;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal static class TrialAuthenticationEndpoints
{
    public static async Task<IResult> LoginAsync(
        HttpContext context,
        TrialAuthenticationServices authentication,
        TrialBrowserCookieCodec cookies,
        TrialLoginLimiter limiter)
    {
        using var admission = limiter.Acquire();
        if (!admission.IsAllowed)
        {
            context.Response.Headers.RetryAfter = admission.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return TrialAuthenticationResponses.Error(429, "rate_limited", "登录尝试过于频繁，请稍后重试。");
        }

        try
        {
            using var request = await TrialLoginRequest.ReadAsync(context.Request, context.RequestAborted)
                .ConfigureAwait(false);
            var result = await authentication.LocalAuthentication.SignInAsync(
                request.AccountName, request.Secret, context.RequestAborted).ConfigureAwait(false);
            if (result.Status != LocalSignInStatus.Succeeded || result.Session is null)
            {
                return TrialAuthenticationResponses.Error(401, "invalid_credentials", "账号或口令不正确。");
            }

            using var session = result.Session;
            var identity = await authentication.BrowserSessions.AuthenticateForMutationAsync(
                session.SessionToken, session.CsrfToken, context.RequestAborted).ConfigureAwait(false);
            if (identity.Identity is null)
            {
                return TrialAuthenticationResponses.AuthenticationRequired();
            }

            cookies.Write(context.Response, session);
            return TrialAuthenticationResponses.Session(
                identity.Identity,
                new TrialBrowserTicket(session.SessionToken, session.CsrfToken, session.AbsoluteExpiresAt));
        }
        catch (TrialLoginRequestException error)
        {
            return TrialAuthenticationResponses.Error(error.StatusCode, "invalid_request", "登录请求格式不正确。");
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return TrialAuthenticationResponses.Error(400, "invalid_request", "登录请求格式不正确。");
        }
    }

    public static IResult Session(HttpContext context)
    {
        var current = TrialAuthenticatedRequest.Get(context);
        return current is null
            ? TrialAuthenticationResponses.AuthenticationRequired()
            : TrialAuthenticationResponses.Session(current.Identity, current.Ticket);
    }

    public static async Task<IResult> LogoutAsync(HttpContext context, TrialAuthenticationServices authentication)
    {
        var current = TrialAuthenticatedRequest.Get(context);
        if (current is null)
        {
            return TrialAuthenticationResponses.AuthenticationRequired();
        }

        var revoked = await authentication.BrowserSessions.SignOutAsync(
            current.Ticket.SessionToken, current.Ticket.CsrfToken, context.RequestAborted).ConfigureAwait(false);
        if (!revoked)
        {
            return TrialAuthenticationResponses.AuthenticationRequired();
        }

        TrialBrowserCookieCodec.Clear(context.Response);
        return Results.NoContent();
    }
}
