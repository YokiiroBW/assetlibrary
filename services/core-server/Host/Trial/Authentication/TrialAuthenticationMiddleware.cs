using AssetLibrary.CoreServer.Hosting.Trial.Preview;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed class TrialAuthenticationMiddleware(
    RequestDelegate next,
    TrialAuthenticationServices authentication,
    TrialBrowserCookieCodec cookies,
    TrialRequestTrust trust)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/assetlink/v1"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (context.Request.Headers.ContainsKey("Authorization"))
        {
            await context.RequestServices.GetRequiredService<ServiceReadAuthenticationHandler>()
                .InvokeAsync(context, next).ConfigureAwait(false);
            return;
        }

        TrialAuthenticationResponses.PreventCaching(context.Response);
        if (!trust.Allows(context.Request))
        {
            await TrialAuthenticationResponses.Forbidden(context).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var originalCancellation = context.RequestAborted;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(originalCancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(TrialImageEndpointMetadata.Applies(context) ? 15 : 5));
        context.RequestAborted = deadline.Token;
        try
        {
            if (context.Request.Path == "/assetlink/v1/auth/login" && HttpMethods.IsPost(context.Request.Method))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            await AuthenticateAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (originalCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (deadline.IsCancellationRequested && TrialImageEndpointMetadata.Applies(context) && !context.Response.HasStarted)
        {
            context.RequestAborted = originalCancellation;
            await TrialImageResponses.Failure(context, ImagePreviewFailure.Timeout).ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (Exception) when (!context.Response.HasStarted)
        {
            context.RequestAborted = originalCancellation;
            await TrialAuthenticationResponses.Unavailable(context).ExecuteAsync(context).ConfigureAwait(false);
        }
        finally
        {
            TrialAuthenticatedRequest.Clear(context);
            context.RequestAborted = originalCancellation;
        }
    }

    private async Task AuthenticateAsync(HttpContext context)
    {
        using var ticket = cookies.Read(context.Request);
        if (ticket is null)
        {
            TrialBrowserCookieCodec.Clear(context.Response);
            await AuthenticationRequired(context).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        if (HttpMethods.IsPost(context.Request.Method) && !TrialRequestTrust.MatchesCsrf(context.Request, ticket))
        {
            await TrialAuthenticationResponses.Forbidden(context).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var result = await authentication.BrowserSessions.AuthenticateForMutationAsync(
            ticket.SessionToken, ticket.CsrfToken, context.RequestAborted).ConfigureAwait(false);
        if (result.Identity is null)
        {
            TrialBrowserCookieCodec.Clear(context.Response);
            await AuthenticationRequired(context).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        new TrialAuthenticatedRequest(result.Identity, ticket).Attach(context);
        await next(context).ConfigureAwait(false);
    }

    private static IResult AuthenticationRequired(HttpContext context) => TrialImageEndpointMetadata.Applies(context)
        ? TrialImageResponses.Unauthenticated(context) : TrialAuthenticationResponses.AuthenticationRequired(context);
}
