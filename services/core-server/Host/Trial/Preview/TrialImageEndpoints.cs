using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.CoreServer.Hosting.Trial.Preview;

internal sealed class TrialImageEndpointMetadata
{
    public static bool Applies(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<TrialImageEndpointMetadata>() is not null;
}

internal static class TrialImageEndpoints
{
    public static void Map(WebApplication application)
    {
        application.MapGet("/assetlink/v1/libraries/{library_id}/entries/{entry_id}/image", ReadAsync)
            .WithMetadata(new TrialImageEndpointMetadata());
    }

    private static async Task ReadAsync(HttpContext context, string library_id, string entry_id)
    {
        var request = TrialImageRequest.Parse(context, library_id, entry_id);
        if (request is null)
        {
            await TrialAuthenticationResponses.Error(400, "invalid_request", "图片请求无效。", context).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        var authenticated = TrialAuthenticatedRequest.Get(context);
        if (authenticated is null)
        {
            await TrialImageResponses.Unauthenticated(context).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        try
        {
            var service = context.RequestServices.GetRequiredService<AuthorizedImagePreviewService>();
            await using var lease = await service.GetAsync(request.Authorize(authenticated.Identity.Subject),
                request.Variant, context.RequestAborted).ConfigureAwait(false);
            if (!await TrialImageSession.RevalidateAsync(context, authenticated).ConfigureAwait(false))
            {
                TrialBrowserCookieCodec.Clear(context.Response);
                await TrialImageResponses.Unauthenticated(context).ExecuteAsync(context).ConfigureAwait(false);
                return;
            }
            await lease.VerifySourceAsync(context.RequestAborted).ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "image/png";
            context.Response.ContentLength = lease.Png.Length;
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            await context.Response.Body.WriteAsync(lease.Png, context.RequestAborted).ConfigureAwait(false);
        }
        catch (ImagePreviewException failure) when (!context.Response.HasStarted)
        {
            await TrialImageResponses.Failure(context, failure.Failure).ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (TimeoutException) when (!context.Response.HasStarted)
        {
            await TrialImageResponses.Failure(context, ImagePreviewFailure.Timeout).ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (Exception) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            await TrialImageResponses.Failure(context, ImagePreviewFailure.Unavailable).ExecuteAsync(context).ConfigureAwait(false);
        }
    }

}
