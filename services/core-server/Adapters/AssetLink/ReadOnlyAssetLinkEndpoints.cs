using System.Text;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

public static class ReadOnlyAssetLinkEndpoints
{
    public const int MaximumRequestBytes = AssetLinkRequestBody.MaximumBytes;

    public static IServiceCollection AddAssetLibraryReadOnlyGateway(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAuthorizedReadModelQuery, PostgresAuthorizedReadModelQuery>();
        services.AddSingleton<ReadOnlyBrowseService>();
        services.AddSingleton<ReadOnlyAssetLinkProtocol>();
        return services;
    }

    public static IEndpointConventionBuilder MapAssetLibraryReadOnlyGateway(
        this IEndpointRouteBuilder endpoints)
        => endpoints.MapAssetLibraryControl(async (context, payload, token) =>
            await context.RequestServices.GetRequiredService<ReadOnlyAssetLinkProtocol>()
                .HandleAsync(context.User, payload, token).ConfigureAwait(false));

    public static IEndpointConventionBuilder MapAssetLibraryControl(
        this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string, CancellationToken, ValueTask<AssetLinkProtocolResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);
        return endpoints.MapPost(
            "/assetlink/v1/control",
            async (HttpContext context) =>
            {
                AssetLinkProtocolResponse response;
                try
                {
                    var payload = await AssetLinkRequestBody.ReadAsync(
                        context.Request,
                        context.RequestAborted).ConfigureAwait(false);
                    response = await handler(
                        context,
                        payload,
                        context.RequestAborted).ConfigureAwait(false);
                }
                catch (RequestBodyTooLargeException)
                {
                    response = ReadOnlyAssetLinkProtocol.TransportError(
                        413,
                        "request_too_large",
                        "The request body is too large.");
                }
                catch (DecoderFallbackException)
                {
                    response = ReadOnlyAssetLinkProtocol.TransportError(
                        400,
                        "invalid_request",
                        "The request body is not valid UTF-8.");
                }
                catch (UnsupportedRequestMediaTypeException)
                {
                    response = ReadOnlyAssetLinkProtocol.TransportError(
                        415,
                        "unsupported_media_type",
                        "A JSON request body is required.");
                }

                return Results.Text(
                    response.Json,
                    "application/json",
                    Encoding.UTF8,
                    response.StatusCode);
            });
    }
}
