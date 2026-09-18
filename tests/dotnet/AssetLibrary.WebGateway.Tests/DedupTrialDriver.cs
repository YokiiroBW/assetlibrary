using System.Net;
using System.Text.Json.Nodes;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// Drives the exact-duplicate workbench exactly as the page does: one authorized HTTP call per operation,
/// against the real host, with the real administrator session and the composition root's own dedup
/// services behind it. It polls the way the page polls — asking again until the durable task settles —
/// and it never reaches into the registry, so what it asserts is what a browser could observe.
/// </summary>
internal sealed class DedupTrialDriver(TrialHostIntegrationFixture host, TrialHostIntegrationSession session)
{
    private const string Prefix = "/assetlink/v1/dedup/";

    /// <summary>
    /// The interval the read-only trial's worker renews a lease on, which is the trial's own composition
    /// value. An attempt on the trial's synthetic source outlives several of them, which is what makes the
    /// renewal observable from outside the process.
    /// </summary>
    public static TimeSpan HeartbeatInterval { get; } = TimeSpan.FromMilliseconds(100);

    /// <summary>The version text of one report, which is the job's own identity plus its generation.</summary>
    public static string AnalysisVersion(Guid taskId, long generation) => $"{taskId:D}:{generation}";

    /// <summary>
    /// A caller with no session is refused before any analysis is started or read. The refusal comes from
    /// the shared authentication middleware, which answers an anonymous request with 401 rather than
    /// letting it reach the endpoint's own administrator check, and every operation is behind it — so a
    /// caller without a live administrator session can neither start work nor read what an administrator
    /// already produced.
    /// </summary>
    public async Task AssertRefusesAnUnauthorizedCallerAsync()
    {
        var library = Guid.NewGuid();
        var task = Guid.NewGuid();
        foreach (var (operation, body) in new (string, JsonObject)[]
                 {
                     ("start", new JsonObject { ["library_id"] = library.ToString("D"), ["operation_key"] = Guid.NewGuid().ToString("D") }),
                     ("status", new JsonObject { ["library_id"] = library.ToString("D"), ["task_id"] = task.ToString("D") }),
                     ("results", new JsonObject { ["library_id"] = library.ToString("D"), ["task_id"] = task.ToString("D") }),
                     ("cancel", new JsonObject { ["library_id"] = library.ToString("D"), ["operation_key"] = Guid.NewGuid().ToString("D") }),
                     ("revalidate", new JsonObject { ["library_id"] = library.ToString("D"), ["task_id"] = task.ToString("D") }),
                     ("export", new JsonObject { ["library_id"] = library.ToString("D"), ["task_id"] = task.ToString("D") }),
                 })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Prefix + operation)
            {
                Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
            };
            // The same origin the page sends, so the answer is about the missing session rather than about
            // a header the browser would always have added.
            request.Headers.Add("Origin", host.Configuration.PublicOrigin);
            using var response = await host.Client.SendAsync(request);
            Assert.AreEqual(
                HttpStatusCode.Unauthorized,
                response.StatusCode,
                $"An anonymous {operation} call is refused.");
            var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            Assert.AreEqual("authentication_required", payload["code"]!.GetValue<string>(), operation);
            // The refusal states nothing about the library or the task it named.
            Assert.IsNull(payload["error"], $"An anonymous {operation} answer must not describe the request it refused.");
        }
    }

    /// <summary>Registers one controlled source, the same way the trial's registration dialog does.</summary>
    public async Task<Guid> RegisterAsync(string root, string displayName)
    {
        var registered = await TrialHostIntegrationHttp.ControlAsync(host, session, "libraries.register", new JsonObject
        {
            ["source_key"] = "fixtures",
            ["display_name"] = displayName,
            ["root_path"] = root,
        }, Guid.NewGuid());
        Assert.AreEqual(200, registered.Status, registered.Payload.ToJsonString());
        return Guid.Parse(registered.Payload["body"]!["library_id"]!.GetValue<string>());
    }

    public Task<DedupTrialAnswer> StartAsync(Guid library, Guid operationKey) =>
        CallAsync("start", Body(library, operationKey));

    public Task<DedupTrialAnswer> JobAsync(Guid library, Guid taskId) =>
        CallAsync("status", Body(library, taskId: taskId));

    public Task<DedupTrialAnswer> ResultsAsync(Guid library, Guid taskId) =>
        CallAsync("results", Body(library, taskId: taskId));

    public Task<DedupTrialAnswer> CancelAsync(Guid library, Guid operationKey) =>
        CallAsync("cancel", Body(library, operationKey));

    public Task<DedupTrialAnswer> ExportAsync(Guid library, Guid taskId, string version, string digest)
    {
        var body = Body(library, taskId: taskId);
        body["analysis_version"] = version;
        body["plan_digest"] = digest;
        return CallAsync("export", body);
    }

    public Task<DedupTrialAnswer> ExportWithVersionAsync(Guid library, Guid taskId, string version)
    {
        var body = Body(library, taskId: taskId);
        body["analysis_version"] = version;
        return CallAsync("export", body);
    }

    /// <summary>Asks for a recheck of one retained version, which answers with a durable task receipt.</summary>
    public Task<DedupTrialAnswer> RevalidateAsync(Guid library, Guid taskId, string digest)
    {
        var body = Body(library, taskId: taskId);
        body["plan_digest"] = digest;
        return CallAsync("revalidate", body);
    }

    public Task<DedupTrialAnswer> RecheckAsync(Guid library, Guid taskId, Guid recheckTaskId)
    {
        var body = Body(library, taskId: taskId);
        body["recheck_task_id"] = recheckTaskId.ToString("D");
        return CallAsync("revalidate", body);
    }

    /// <summary>A recheck asked for a version that is no longer retained, so the call itself is refused.</summary>
    public Task<DedupTrialAnswer> RevalidateRefusalAsync(Guid library, Guid taskId) =>
        CallAsync("revalidate", Body(library, taskId: taskId));

    /// <summary>Waits until the job's own answer carries a readable report, the way the page waits.</summary>
    public async Task<DedupTrialAnswer> WaitForReportAsync(Guid library, Guid taskId) =>
        await WaitForReportAsync(library, taskId, null);

    /// <summary>
    /// Waits until the job's own answer carries a readable report, and records what the page would have
    /// observed while it waited. The observations are what let a test state that the attempt really stayed
    /// alive under its lease: a durable task's <c>updated_at</c> only moves when its state changes or its
    /// lease is renewed, and the worker renews it on its own heartbeat interval.
    /// </summary>
    public async Task<DedupTrialAnswer> WaitForReportAsync(
        Guid library,
        Guid taskId,
        List<DedupTrialObservation>? observations)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        while (!deadline.IsCancellationRequested)
        {
            var answer = await JobAsync(library, taskId);
            Assert.AreEqual(200, answer.Status, answer.Payload.ToJsonString());
            observations?.Add(new DedupTrialObservation(
                answer.State,
                answer.CancellationRequested,
                answer.UpdatedAt,
                answer.ReportAvailable));
            // The page is done when there is a report to read. The durable task is a separate fact and
            // settles a moment later, so it is only asserted once it is terminal.
            if (answer.ReportAvailable && answer.State is "succeeded" or "failed" or "cancelled")
            {
                return answer;
            }

            Assert.AreNotEqual("failed", answer.State, $"The analysis failed: {answer.FailureCode}");
            // A page polls far faster than a person can read: this is what lets a test observe a job that
            // is still being worked on rather than only its finished state. The interval is short because
            // an attempt on the trial's synthetic source is short — a slower poll would only ever see the
            // job before it started and after it finished, and could not state what happened in between.
            await Task.Delay(10, deadline.Token);
        }

        Assert.Fail($"No report became readable for {taskId:D}.");
        return null!;
    }

    /// <summary>Waits until a recheck's durable task has an outcome, so a page stops polling.</summary>
    public async Task<DedupTrialAnswer> WaitForRecheckAsync(Guid library, Guid taskId, Guid recheckTaskId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        while (!deadline.IsCancellationRequested)
        {
            var answer = await RecheckAsync(library, taskId, recheckTaskId);
            Assert.AreEqual(200, answer.Status, answer.Payload.ToJsonString());
            if (answer.State != "pending")
            {
                return answer;
            }

            await Task.Delay(150, deadline.Token);
        }

        Assert.Fail($"The recheck {recheckTaskId:D} never reached an outcome.");
        return null!;
    }

    /// <summary>Waits until a job can no longer change, which is what a cancelled attempt has to reach.</summary>
    public async Task<DedupTrialAnswer> WaitForTerminalAsync(Guid library, Guid taskId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        while (!deadline.IsCancellationRequested)
        {
            var answer = await JobAsync(library, taskId);
            Assert.AreEqual(200, answer.Status, answer.Payload.ToJsonString());
            if (answer.State is "succeeded" or "failed" or "cancelled")
            {
                return answer;
            }

            await Task.Delay(150, deadline.Token);
        }

        Assert.Fail($"The job {taskId:D} never settled.");
        return null!;
    }

    private async Task<DedupTrialAnswer> CallAsync(string operation, JsonObject body)
    {
        var response = await TrialHostIntegrationHttp.SendAsync(
            host,
            session,
            HttpMethod.Post,
            Prefix + operation,
            body.ToJsonString());
        return new DedupTrialAnswer(response.Status, response.Payload);
    }

    private static JsonObject Body(Guid library, Guid? operationKey = null, Guid? taskId = null)
    {
        var body = new JsonObject
        {
            ["request_id"] = Guid.NewGuid().ToString("D"),
            ["library_id"] = library.ToString("D"),
        };
        if (operationKey.HasValue)
        {
            body["operation_key"] = operationKey.Value.ToString("D");
        }

        if (taskId.HasValue)
        {
            body["task_id"] = taskId.Value.ToString("D");
        }

        return body;
    }
}

/// <summary>
/// One answer of the workbench, read through the field names the page's decoder reads. A refused call and
/// an answer about nothing are different values here, so a test can never pass by reading an error as an
/// empty result set.
/// </summary>
/// <remarks>
/// The six dedup operations answer with the operation's own document at the top level, while a failure is
/// the shared transport error envelope. Both are read through the same accessors, so a test states which
/// fact it wants rather than which of the two shapes it happened to receive.
/// </remarks>
internal sealed record DedupTrialAnswer(int Status, JsonObject Payload)
{
    private JsonObject Answer => Payload["body"] as JsonObject ?? Payload;

    private JsonNode? Field(string name) => Answer[name] ?? Payload[name];

    private JsonNode Required(string name) =>
        Field(name) ?? throw new InvalidOperationException($"The answer has no {name}: {Payload.ToJsonString()}");

    public Guid TaskId => Guid.Parse(Required("task_id").GetValue<string>());

    public string State => Required("state").GetValue<string>();

    public bool ReportAvailable => Required("report_available").GetValue<bool>();

    public string AnalysisVersion => Required("analysis_version").GetValue<string>();

    /// <summary>The digest of the plan this answer is about: an exported document's own plan digest.</summary>
    public string DocumentPlanDigest => Required("plan_digest").GetValue<string>();

    public DateTimeOffset CreatedAt => Required("created_at").GetValue<DateTimeOffset>();

    public DateTimeOffset UpdatedAt => Required("updated_at").GetValue<DateTimeOffset>();

    public bool CancellationRequested => Required("cancellation_requested").GetValue<bool>();

    public string? FailureCode => Field("failure_code")?.GetValue<string>();

    public string PlanStatus => Required("summary")!["plan"]!["status"]!.GetValue<string>();

    public int DuplicateGroupCount => Required("summary")!["duplicate_group_count"]!.GetValue<int>();

    public string PlanDigest => Required("summary")!["plan_digest"]!.GetValue<string>();

    /// <summary>The digest a recheck answer names: it is the plan's own digest, not a summary's.</summary>
    public string RecheckPlanDigest => Required("plan_digest").GetValue<string>();

    /// <summary>Why a page found no report: the report's own answer, never an absent key.</summary>
    public string? SummaryFailureCode => Required("summary")!["plan"]!["failure_code"]?.GetValue<string>();

    public IReadOnlyList<JsonObject> Groups =>
        [.. Required("groups")!.AsArray().Select(node => node!.AsObject())];

    public int Total => Required("total")!.GetValue<int>();

    public int AnalyzedFiles => Required("summary")!["statistics"]!["analyzed_files"]!.GetValue<int>();

    public int NotReadFiles => Required("summary")!["statistics"]!["not_read_files"]!.GetValue<int>();

    public int FailedFiles => Required("summary")!["statistics"]!["failed_files"]!.GetValue<int>();

    public int SkippedFiles => Required("summary")!["statistics"]!["skipped_files"]!.GetValue<int>();

    public int ByteDuplicateFiles => Required("summary")!["statistics"]!["byte_duplicate_files"]!.GetValue<int>();

    public long ReadBytes => Required("summary")!["statistics"]!["read_bytes"]!.GetValue<long>();

    public bool Completed => Required("completed")!.GetValue<bool>();

    public bool PlanStillCurrent => Required("plan_still_current")!.GetValue<bool>();

    public Guid RecheckTaskId => Guid.Parse(Required("recheck_task_id")!.GetValue<string>());

    public bool GrantsFileOperation => Required("grants_file_operation")!.GetValue<bool>();

    public int GroupCount => Required("groups")!.AsArray().Count;

    /// <summary>The error code of a refused call, or null when the call was answered.</summary>
    public string? Code => Payload["error"]?["code"]?.GetValue<string>()
        ?? Payload["body"]?["error"]?["code"]?.GetValue<string>();
}

/// <summary>
/// What one poll of a job observed, in the fields the page's own decoder reads. The update time is kept
/// because it is the only thing on the wire that can move while a job's state does not.
/// </summary>
internal sealed record DedupTrialObservation(
    string State,
    bool CancellationRequested,
    DateTimeOffset UpdatedAt,
    bool ReportAvailable);

/// <summary>The group and member accessors a results assertion needs, kept beside the answer record.</summary>
internal static class DedupTrialAnswerGroups
{
    public static IReadOnlyList<JsonObject> Members(this JsonObject group) =>
        [.. group["members"]!.AsArray().Select(node => node!.AsObject())];
}
