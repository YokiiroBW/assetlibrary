using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.AssetLink;
using AssetLibrary.CoreServer.Adapters.AssetLink;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Contracts;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal sealed class TrialManagementGateway(
    ReadOnlyAssetLinkProtocol reads,
    TrialLibraryServices libraries,
    IInitialScanCoordinator scans)
{
    public async ValueTask<AssetLinkProtocolResponse> HandleAsync(HttpContext context, string payload, CancellationToken token)
    {
        var requestId = "unknown";
        try
        {
            if (Parse(payload) is not ControlRequestMessage request)
            {
                return Error(400, requestId, "invalid_request");
            }

            requestId = request.RequestId;
            if (!IsManagement(request.Operation))
            {
                return await reads.HandleAsync(context.User, payload, token).ConfigureAwait(false);
            }

            if (!TrialAuthentication.TryGetCurrentAdministrator(context, out var administrator))
            {
                return Error(403, requestId, "permission_denied");
            }

            if (string.IsNullOrEmpty(requestId) || requestId.Length > 128 || requestId.Any(char.IsControl)
                || request.CancelOf is not null || request.TimeoutMs is < 1 or > 5000)
            {
                return Error(400, "unknown", "invalid_request");
            }

            var result = await ExecuteAsync(request, administrator!.PrincipalId, token).ConfigureAwait(false);
            return ReadOnlyAssetLinkProtocol.ControlResult(requestId, result);
        }
        catch (ReadOnlyTrialException exception)
        {
            return Error(Status(exception.Code), requestId, exception.Code);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
        {
            return Error(400, requestId, "invalid_request");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Error(503, requestId, "service_unavailable");
        }
    }

    private async ValueTask<JsonObject> ExecuteAsync(ControlRequestMessage request, Guid principalId, CancellationToken token)
    {
        if (request.Operation == "storage_sources.list")
        {
            return new JsonObject
            {
                ["sources"] = new JsonArray(libraries.Sources.Select(source => (JsonNode)new JsonObject
                {
                    ["source_key"] = source.SourceKey, ["display_name"] = source.DisplayName,
                }).ToArray()),
            };
        }

        if (request.Operation == "libraries.register")
        {
            var id = await libraries.Registration.RegisterAsync(
                new LibraryRegistrationRequest(Text(request.Body, "source_key"), Text(request.Body, "display_name"), Text(request.Body, "root_path")),
                Operation(request, principalId), token).ConfigureAwait(false);
            return new JsonObject { ["library_id"] = id.Value.ToString("D") };
        }

        var libraryId = new LibraryId(Identifier(request.Body, "library_id"));
        var scan = request.Operation switch
        {
            "library_scans.get" => await scans.GetAsync(libraryId, token).ConfigureAwait(false),
            "library_scans.start" => await scans.StartAsync(libraryId, Operation(request, principalId), token).ConfigureAwait(false),
            "library_scans.cancel" => await scans.CancelAsync(libraryId, Identifier(request.Body, "task_id"), Operation(request, principalId), token).ConfigureAwait(false),
            _ => throw new ArgumentException("Unsupported trial operation."),
        };
        return new JsonObject { ["library_id"] = libraryId.Value.ToString("D"), ["scan"] = TrialScanJson.Serialize(scan) };
    }

    private static bool IsManagement(string operation) => operation is
        "storage_sources.list" or "libraries.register" or "library_scans.get" or "library_scans.start" or "library_scans.cancel";

    private static ManagementOperation Operation(ControlRequestMessage request, Guid principalId) =>
        Guid.TryParseExact(request.IdempotencyKey, "D", out var key) && key != Guid.Empty
            ? new ManagementOperation(principalId, key) : throw new ArgumentException("A valid operation identity is required.");

    private static string Text(JsonObject body, string name) =>
        body[name] is JsonValue value && value.TryGetValue<string>(out var text) && text is not null
            ? text : throw new ArgumentException("A required field is missing.");

    private static AssetLinkMessage Parse(string payload)
    {
        try
        {
            return AssetLinkCodec.Parse(payload);
        }
        catch (InvalidOperationException)
        {
            throw new JsonException("The control envelope is invalid.");
        }
    }

    private static Guid Identifier(JsonObject body, string name) =>
        Guid.TryParseExact(Text(body, name), "D", out var value) && value != Guid.Empty
            ? value : throw new ArgumentException("A valid identifier is required.");

    private static int Status(string code) => code switch
    {
        "library_not_found" or "scan_not_found" => 404,
        "idempotency_conflict" or "root_overlap" or "already_indexed" or "scan_already_running" => 409,
        "root_not_allowed" or "storage_source_not_allowed" => 403,
        "root_unavailable" or "root_inaccessible" or "storage_unavailable" or "worker_timed_out" => 503,
        "invalid_request" => 400,
        _ => 503,
    };

    private static AssetLinkProtocolResponse Error(int status, string requestId, string code) =>
        ReadOnlyAssetLinkProtocol.ControlError(status, requestId, code, status switch
        {
            403 => "当前请求没有操作权限。",
            404 => "请求的资源不可用。",
            409 => "操作与当前状态或已有请求冲突。",
            503 => "服务或存储暂时不可用，请稍后重试。",
            _ => "请求格式不正确。",
        });
}

internal static class TrialScanJson
{
    public static JsonObject? Serialize(ScanTaskView? scan) => scan is null ? null : new JsonObject
    {
        ["task_id"] = scan.TaskId.ToString("D"),
        ["scan_id"] = scan.ScanId?.ToString("D"),
        ["state"] = scan.State.ToString().ToLowerInvariant(),
        ["cancellation_requested"] = scan.CancellationRequested,
        ["observed_entries"] = scan.ObservedEntries,
        ["committed_entries"] = scan.CommittedEntries,
        ["started_at"] = scan.StartedAt,
        ["finished_at"] = scan.FinishedAt,
        ["failure_code"] = scan.FailureCode,
        ["can_cancel"] = scan.CanCancel,
        ["can_retry"] = scan.CanRetry,
    };
}
