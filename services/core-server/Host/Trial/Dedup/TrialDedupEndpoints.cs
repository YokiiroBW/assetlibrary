using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Adapters.AssetLink;
using AssetLibrary.Modules.AssetIdentity.Dedup.Application;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Domain;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.CoreServer.Hosting.Trial;

/// <summary>
/// HTTP adapter of the exact-duplicate workbench. It validates a request, re-checks that the caller is
/// still an administrator and that the named library is still an authorized source, calls one
/// application operation and serializes the answer. It contains no dedup rule and never decides what a
/// duplicate is: every state machine, hash comparison and plan rule lives in AssetIdentity.
/// </summary>
internal static class TrialDedupEndpoints
{
    private const string Prefix = "/assetlink/v1/dedup/";

    private static IReadOnlyList<string> Operations =>
        ["start", "status", "results", "cancel", "revalidate", "export"];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (var operation in Operations)
        {
            var name = operation;
            endpoints.MapPost(
                Prefix + name,
                (Delegate)((HttpContext context) => TrialDedupOperations.InvokeAsync(context, name)));
        }
    }
}

/// <summary>
/// The request pipeline of one dedup operation: authentication, bounded body read, library scope
/// re-check, dispatch and error mapping. It is separate from the route map so each part stays small
/// enough to review on its own.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "An HTTP adapter's whole job is to name every operation and its result type once. The count is the size of the authorized surface, and its only calls into the core are the coordinator's public operations.")]
internal static class TrialDedupOperations
{
    public static async Task<IResult> InvokeAsync(HttpContext context, string operation)
    {
        try
        {
            // The hidden button is not a permission: every single call, including a page read or an
            // export, is authorized here against the live session and the live library scope.
            if (!TrialAuthentication.TryGetCurrentAdministrator(context, out _))
            {
                return Fail(403, "permission_denied");
            }

            var payload = await AssetLinkRequestBody.ReadAsync(context.Request, context.RequestAborted)
                .ConfigureAwait(false);
            var request = TrialDedupRequest.Parse(context, payload);
            var dedup = context.RequestServices.GetRequiredService<TrialDedupServices>();
            var source = await ResolveAsync(dedup, request.RequireLibraryId(), context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Text(
                TrialDedupJson.Serialize(await DispatchAsync(operation, dedup, request, source, context.RequestAborted)
                    .ConfigureAwait(false)),
                "application/json",
                Encoding.UTF8,
                200);
        }
        catch (RequestBodyTooLargeException)
        {
            return Fail(413, "request_too_large");
        }
        catch (UnsupportedRequestMediaTypeException)
        {
            return Fail(415, "unsupported_media_type");
        }
        catch (DecoderFallbackException)
        {
            return Fail(400, "invalid_request");
        }
        catch (ReadOnlyTrialException exception)
        {
            return Fail(TrialDedupJson.Status(exception.Code), exception.Code);
        }
        catch (InvalidDedupCursorException)
        {
            return Fail(400, "invalid_cursor");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
        {
            return Fail(400, "invalid_request");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(503, "service_unavailable");
        }
    }

    private static async ValueTask<JsonObject> DispatchAsync(
        string operation,
        TrialDedupServices dedup,
        TrialDedupRequest request,
        DedupResolvedSource source,
        CancellationToken cancellationToken)
    {
        var taskId = request.RequireTaskId();
        return operation switch
        {
            "start" => TrialDedupJson.Job(await dedup.Jobs.StartAsync(
                source,
                request.Limits(),
                request.Retry,
                request.Operation(source.LibraryId.Value),
                cancellationToken).ConfigureAwait(false)),
            "status" => TrialDedupJson.Job(await dedup.Jobs.GetAsync(source, taskId, cancellationToken)
                .ConfigureAwait(false)),
            "results" => TrialDedupPageJson.Page(await dedup.Jobs.ResultsAsync(
                source,
                taskId,
                new DedupPageRequest(request.Cursor, request.GroupKey, request.Kind(), request.PageSize),
                cancellationToken).ConfigureAwait(false)),
            "cancel" => TrialDedupJson.Job(await dedup.Jobs.CancelAsync(
                source,
                request.Operation(source.LibraryId.Value),
                cancellationToken).ConfigureAwait(false)),
            "revalidate" => TrialDedupPageJson.Recheck(await dedup.Jobs.RevalidateAsync(
                source,
                taskId,
                request.ExpectedDigest,
                request.Operation(source.LibraryId.Value),
                cancellationToken).ConfigureAwait(false)),
            "export" => TrialDedupExportJson.Export(await dedup.Jobs.ExportAsync(
                source,
                taskId,
                request.ExpectedVersion,
                request.ExpectedDigest,
                cancellationToken).ConfigureAwait(false)),
            _ => throw new ReadOnlyTrialException("invalid_request"),
        };
    }

    /// <summary>
    /// Resolves the library the caller names into the root the composition root authorizes. The value
    /// comes from the library query, so a request body can never name a directory to read.
    /// </summary>
    private static async ValueTask<DedupResolvedSource> ResolveAsync(
        TrialDedupServices dedup,
        Guid libraryId,
        CancellationToken cancellationToken)
    {
        var id = new LibraryId(libraryId);
        var target = await dedup.Libraries.FindAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new ReadOnlyTrialException("library_not_found");
        var registered = await dedup.Scopes.RegisteredRootsAsync(cancellationToken).ConfigureAwait(false);
        if (!registered.Any(root => DedupScopePolicy.IsWithin(target.Root, root)))
        {
            throw new ReadOnlyTrialException("library_not_allowed");
        }

        return new DedupResolvedSource(
            DedupSourceId.New(),
            id,
            target.Root,
            target.LibraryId.Value.ToString("D"));
    }

    private static IResult Fail(int status, string code) => Results.Text(
        TrialDedupJson.Serialize(TrialDedupJson.Error(status, code)),
        "application/json",
        Encoding.UTF8,
        status);
}
