using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.CoreServer.Hosting.Trial;

/// <summary>
/// The parsed body of one dedup operation. It holds only values the caller is allowed to choose: an
/// operation key, a report version, a filter and a cursor. A physical path is deliberately not
/// representable here, so a page cannot ask the analyzer to read a directory it names.
/// </summary>
internal sealed record TrialDedupRequest(
    JsonObject Body,
    Guid RequestId,
    Guid? LibraryId,
    Guid? TaskId,
    Guid? OperationKey,
    string? Cursor,
    string? GroupKey,
    int PageSize,
    bool Retry,
    string? ExpectedVersion,
    string? ExpectedDigest)
{
    public static TrialDedupRequest Parse(HttpContext context, string payload)
    {
        var body = JsonNode.Parse(payload) as JsonObject
            ?? throw new JsonException("A JSON object body is required.");
        return new TrialDedupRequest(
            body,
            GuidValue(body, "request_id") ?? Guid.NewGuid(),
            GuidValue(body, "library_id"),
            GuidValue(body, "task_id"),
            GuidValue(body, "operation_key"),
            Text(body, "cursor"),
            Text(body, "group_key"),
            IntValue(body, "page_size") ?? 0,
            BoolValue(body, "retry") ?? false,
            Text(body, "analysis_version"),
            Text(body, "plan_digest"));
    }

    public Guid RequireLibraryId() => LibraryId is { } value && value != Guid.Empty
        ? value
        : throw new ReadOnlyTrialException("invalid_request");

    public Guid RequireTaskId() => TaskId is { } value && value != Guid.Empty
        ? value
        : throw new ReadOnlyTrialException("invalid_request");

    /// <summary>
    /// The caller's operation key. It is required so a retried start replays the same durable job
    /// instead of creating a second one, and so a cancel is attributable to the request that asked.
    /// </summary>
    public Guid RequireOperationKey() => OperationKey is { } value && value != Guid.Empty
        ? value
        : throw new ReadOnlyTrialException("invalid_request");

    public DedupOperation Operation(Guid libraryId) => new(libraryId, RequireOperationKey());

    public DedupFindingKind? Kind() =>
        Body.TryGetPropertyValue("kind", out var node) && node is not null
            ? Enum.TryParse<DedupFindingKind>(node.GetValue<string>(), ignoreCase: true, out var kind)
                ? kind
                : throw new ReadOnlyTrialException("invalid_request")
            : null;

    /// <summary>
    /// The ceilings this run may use. A caller may lower a ceiling but never raise it past the
    /// installation default, so a page cannot turn one analysis into an unbounded read.
    /// </summary>
    public DedupAnalysisLimits Limits() => new DedupAnalysisLimits(
        Math.Min(IntValue(Body, "maximum_files") ?? DedupAnalysisLimits.DefaultMaximumFiles,
            DedupAnalysisLimits.DefaultMaximumFiles),
        Math.Min(LongValue(Body, "maximum_bytes") ?? DedupAnalysisLimits.DefaultMaximumBytes,
            DedupAnalysisLimits.DefaultMaximumBytes),
        Math.Min(IntValue(Body, "maximum_file_bytes") ?? DedupAnalysisLimits.DefaultMaximumFileBytes,
            DedupAnalysisLimits.DefaultMaximumFileBytes)).Validate();

    private static Guid? GuidValue(JsonObject body, string name) =>
        Text(body, name) is { Length: > 0 } text
            ? Guid.TryParseExact(text, "D", out var parsed) ? parsed : throw new JsonException($"Field {name} is not a UUID.")
            : null;

    private static string? Text(JsonObject body, string name) =>
        body.TryGetPropertyValue(name, out var node) && node is not null
            ? node.GetValueKind() == JsonValueKind.String
                ? node.GetValue<string>()
                : throw new JsonException($"Field {name} is not text.")
            : null;

    private static int? IntValue(JsonObject body, string name) =>
        body.TryGetPropertyValue(name, out var node) && node is not null
            ? node.GetValueKind() == JsonValueKind.Number
                ? node.GetValue<int>()
                : throw new JsonException($"Field {name} is not a number.")
            : null;

    private static long? LongValue(JsonObject body, string name) =>
        body.TryGetPropertyValue(name, out var node) && node is not null
            ? node.GetValueKind() == JsonValueKind.Number
                ? node.GetValue<long>()
                : throw new JsonException($"Field {name} is not a number.")
            : null;

    private static bool? BoolValue(JsonObject body, string name) =>
        body.TryGetPropertyValue(name, out var node) && node is not null
            ? node.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                ? node.GetValue<bool>()
                : throw new JsonException($"Field {name} is not a boolean.")
            : null;
}
