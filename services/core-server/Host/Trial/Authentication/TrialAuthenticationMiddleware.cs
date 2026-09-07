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

        TrialAuthenticationResponses.PreventCaching(context.Response);
        if (!trust.Allows(context.Request))
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
            await TrialAuthenticationResponses.AuthenticationRequired(context).ExecuteAsync(context).ConfigureAwait(false);
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
            await TrialAuthenticationResponses.AuthenticationRequired(context).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        new TrialAuthenticatedRequest(result.Identity, ticket).Attach(context);
        await next(context).ConfigureAwait(false);
    }
}
