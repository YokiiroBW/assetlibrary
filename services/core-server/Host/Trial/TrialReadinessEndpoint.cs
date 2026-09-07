namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialReadinessEndpoint
{
    public static void Map(WebApplication application, Guid deploymentId, IDatabaseReadinessProbe readiness)
    {
        application.MapGet("/healthz", () => Results.Json(new
        {
            status = "ok",
            contract = "v01-015/1",
            scope = "read_only_trial",
            production_file_writes_enabled = false,
        }));
        application.MapGet("/readyz", async (HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var result = await readiness.CheckAsync(token).ConfigureAwait(false);
            return Results.Json(new
            {
                status = result.IsReady ? "ready" : "not_ready",
                contract = "v01-015/1",
                scope = "read_only_trial",
                deployment_id = deploymentId,
                database_ready = result.IsReady,
                business_api_ready = result.IsReady,
                production_file_writes_enabled = false,
                reason = result.PublicCode,
            }, statusCode: result.IsReady ? 200 : 503);
        });
    }
}
