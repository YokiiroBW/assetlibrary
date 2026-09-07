using System.Buffers;
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
    public const int MaximumRequestBytes = 64 * 1024;

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
                    if (!context.Request.HasJsonContentType())
                    {
                        throw new UnsupportedRequestMediaTypeException();
                    }

                    var payload = await ReadBoundedBodyAsync(
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

    private static async ValueTask<string> ReadBoundedBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumRequestBytes)
        {
            throw new RequestBodyTooLargeException();
        }

        var writer = new ArrayBufferWriter<byte>();
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (writer.WrittenCount + read > MaximumRequestBytes)
                {
                    throw new RequestBodyTooLargeException();
                }

                writer.Write(buffer.AsSpan(0, read));
            }

            return new UTF8Encoding(false, true).GetString(writer.WrittenSpan);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed class RequestBodyTooLargeException : Exception
    {
    }

    private sealed class UnsupportedRequestMediaTypeException : Exception
    {
    }
}
