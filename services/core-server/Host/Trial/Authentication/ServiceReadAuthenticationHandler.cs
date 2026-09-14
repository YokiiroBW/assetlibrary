using System.Security.Claims;
using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed class ServiceReadAuthenticationHandler(TrialConfiguration configuration, ServiceReadAuthenticationService authentication)
{
    private static readonly object ContextKey = new();

    public static bool IsServiceRequest(HttpContext context) => context.Items.ContainsKey(ContextKey);

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        TrialAuthenticationResponses.PreventCaching(context.Response);
        context.Response.Headers.Vary = "Authorization, Cookie";
        if (!configuration.ServiceReadEnabled || !AllowsTransport(context.Request))
        {
            await TrialAuthenticationResponses.Forbidden(context).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var originalCancellation = context.RequestAborted;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(originalCancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        context.RequestAborted = deadline.Token;
        try
        {
            var header = context.Request.Headers.Authorization;
            if (header.Count != 1 || header[0] is not { } value
                || !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                await TrialAuthenticationResponses.AuthenticationRequired(context).ExecuteAsync(context).ConfigureAwait(false);
                return;
            }
            ServiceReadToken token;
            try { token = ServiceReadToken.Parse(value[7..]); }
            catch (ArgumentException)
            {
                await TrialAuthenticationResponses.AuthenticationRequired(context).ExecuteAsync(context).ConfigureAwait(false);
                return;
            }
            using (token)
            {
                var identity = await authentication.AuthenticateAsync(token, deadline.Token).ConfigureAwait(false);
                if (identity is null)
                {
                    await TrialAuthenticationResponses.AuthenticationRequired(context).ExecuteAsync(context).ConfigureAwait(false);
                    return;
                }
                context.Items[ContextKey] = true;
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, identity.Subject.Value)], "AssetLibrary.ServiceRead"));
                await next(context).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (originalCancellation.IsCancellationRequested) { throw; }
        catch (Exception) when (!context.Response.HasStarted)
        {
            context.RequestAborted = originalCancellation;
            await TrialAuthenticationResponses.Unavailable(context).ExecuteAsync(context).ConfigureAwait(false);
        }
        finally
        {
            context.Items.Remove(ContextKey);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            context.RequestAborted = originalCancellation;
        }
    }

    private bool AllowsTransport(HttpRequest request) => request.IsHttps
        && string.Equals(request.Host.Value, configuration.Origin.Authority, StringComparison.OrdinalIgnoreCase)
        && request.Path == "/assetlink/v1/control" && HttpMethods.IsPost(request.Method)
        && !request.Headers.ContainsKey("Cookie") && !request.Headers.ContainsKey("Origin")
        && !request.Headers.ContainsKey(TrialRequestTrust.CsrfHeader)
        && !request.Headers.Keys.Any(key => key.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase));
}
