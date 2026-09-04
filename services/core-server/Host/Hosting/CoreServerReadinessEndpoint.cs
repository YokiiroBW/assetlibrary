namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerReadinessEndpoint
{
    public static void Map(WebApplication application, IDatabaseReadinessProbe? database)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (database is null)
        {
            application.MapGet("/readyz", CoreServerEndpointPayloads.Ready);
            return;
        }

        application.MapGet(
            "/readyz",
            async (CancellationToken cancellationToken) =>
            {
                var result = await database.CheckAsync(cancellationToken).ConfigureAwait(false);
                return Results.Json(
                    CoreServerEndpointPayloads.Database(result),
                    statusCode: result.IsReady
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status503ServiceUnavailable);
            });
    }
}
