using System.Text;
using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.CoreServer.Hosting.Trial;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// The exact-duplicate workbench on the real stack: the owned PostgreSQL cluster, the real administrator
/// session over HTTPS, the composition root's own dedup services and the hosted worker that claims a real
/// durable lease. It drives the six authorized operations through HTTP and then asserts the two facts a
/// wire test cannot reach — that the run happened under a lease TaskHealth really issued, and that a
/// report which is no longer readable says so instead of looking like an analysis that found nothing.
/// </summary>
/// <remarks>
/// The trial owns one runtime directory, one TLS certificate file and one loopback origin per run, so the
/// two real trials in this assembly must not overlap: a second host building its certificate over the
/// first one's state directory is a conflict about the fixture, not about either product path.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class DedupTrialIntegrationTests
{
    [TestMethod]
    public async Task RealLeaseRunsTheWorkbenchAndStatesWhatIsNoLongerReadable()
    {
        var settings = TrialHostIntegrationSettings.Load();
        var assets = new DedupTrialAssets(settings.RuntimeRoot);
        await using var host = await TrialHostIntegrationFixture.CreateAsync(settings, "state-dedup");
        await TrialOperatorBootstrap.InitializeAsync(host);
        await host.StartAsync();
        var session = await TrialHostIntegrationHttp.SignInAsync(host);
        var dedup = new DedupTrialDriver(host, session);
        var leases = new DedupTrialLeaseReader(host);
        await dedup.AssertRefusesAnUnauthorizedCallerAsync();
        var library = await dedup.RegisterAsync(assets.LibraryRoot, "精确查重验证");

        // 1. start / 2. status: one real analysis, run by the hosted worker under a lease it claimed.
        //    The source is large enough that the attempt outlives several heartbeat intervals, which is
        //    what makes the lease observable from outside the process.
        var first = await dedup.StartAsync(library, Guid.NewGuid());
        Assert.IsTrue(first.State is "queued" or "leased", $"A new analysis starts active, observed {first.State}.");
        Assert.IsFalse(first.ReportAvailable, "A job that has not finished has no report to read.");
        Assert.AreEqual(string.Empty, first.AnalysisVersion, "An unfinished job names no version.");

        // The attempt is really claimed by the worker, and its lease really advances while it runs. The
        // lease is read from the store's own columns rather than inferred from how long the attempt took:
        // a terminal task's timestamps move because it finished, so only a sample taken while it is still
        // leased can show that a renewal happened. The window is wide enough to cover several of the
        // worker's heartbeat intervals, so two live samples that disagree about the lease are a renewal
        // rather than a coincidence of timing.
        var claimed = await leases.WaitForClaimAsync(first.TaskId);
        Assert.AreEqual("trial-dedup", claimed.Owner, "The claim names the worker that holds it.");
        Assert.IsNotNull(claimed.LeaseUntil, "A claimed attempt has a lease window.");
        var window = claimed.LeaseUntil.Value;
        // The job's own answers are polled while the lease is being watched, not after it: an attempt on
        // this source is short, and a reader that only looked once the lease window had closed would only
        // ever see the finished job.
        var observations = new List<DedupTrialObservation>();
        var polling = dedup.WaitForReportAsync(library, first.TaskId, observations);
        var observed = await leases.ObserveWhileActiveAsync(first.TaskId, TimeSpan.FromSeconds(5));
        var analyzed = await polling;
        var renewed = observed.Where(sample => sample.State == "leased").ToArray();
        Assert.IsGreaterThanOrEqualTo(
            2,
            renewed.Length,
            $"The attempt must still be leased while it is sampled, observed {string.Join('/', observed.Select(sample => $"{sample.State}@{sample.HeartbeatAt:HH:mm:ss.fff}"))}.");
        Assert.IsTrue(
            renewed[^1].HeartbeatAt > renewed[0].HeartbeatAt,
            $"The lease must be renewed while the attempt runs: heartbeat {renewed[0].HeartbeatAt:O} → {renewed[^1].HeartbeatAt:O}.");
        Assert.IsTrue(
            renewed[^1].LeaseUntil > window,
            $"A renewal must extend the lease window past the claim's own: {window:O} → {renewed[^1].LeaseUntil:O}.");
        Assert.IsNull(
            renewed[^1].CancellationRequestedAt,
            "Nothing asked for this attempt to be cancelled.");
        Assert.AreEqual(DedupTrialDriver.AnalysisVersion(first.TaskId, 1), analyzed.AnalysisVersion);
        Assert.AreEqual("succeeded", analyzed.State, "A finished analysis is a succeeded durable task.");

        // The attempt was really observed while it was still active — a job that reported a report on the
        // first poll would prove nothing about how it got there.
        Assert.IsGreaterThanOrEqualTo(
            2,
            observations.Count,
            $"The attempt must be observable while it runs, observed {string.Join('/', observations.Select(item => item.State))}.");
        Assert.IsTrue(
            observations[0].State is "queued" or "leased",
            $"The first observation is of an attempt that has not finished, observed {observations[0].State}.");
        Assert.IsFalse(
            observations[0].ReportAvailable,
            "Nothing is readable while the attempt is still active.");
        Assert.IsGreaterThanOrEqualTo(
            2,
            observations.TakeWhile(observation => observation.State is "queued" or "leased").Count(),
            $"The attempt must be observed more than once while it is still active, observed {string.Join('/', observations.Select(item => item.State))}.");

        // 3. results: the page names its own version and carries the duplicate evidence.
        var page = await dedup.ResultsAsync(library, first.TaskId);
        Assert.AreEqual(analyzed.AnalysisVersion, page.AnalysisVersion, "A page must name the version it read.");
        Assert.IsTrue(page.ReportAvailable, "A finished analysis is readable.");
        Assert.IsGreaterThan(0, page.Total);
        Assert.IsGreaterThan(0, page.DuplicateGroupCount, "The synthetic library really contains a duplicate pair.");
        // Every synthetic file is accounted for: read and classified, or read and recognised as a byte
        // duplicate of one already read. The analyser keeps both a "not read" and a "skipped" counter over
        // the second group, so the check is what the counters have to add up to, that nothing is reported
        // as unreadable, and that the bytes really were read — not which bucket each file lands in.
        Assert.AreEqual(
            DedupTrialAssets.ExpectedFileCount,
            page.AnalyzedFiles + page.SkippedFiles,
            $"Every synthetic file must be classified: analyzed {page.AnalyzedFiles}, " +
            $"duplicates {page.ByteDuplicateFiles}, skipped {page.SkippedFiles}, " +
            $"not read {page.NotReadFiles}, failed {page.FailedFiles}, {page.ReadBytes} bytes read.");
        Assert.AreEqual(
            page.SkippedFiles,
            page.NotReadFiles,
            "The files this analysis chose not to read are the ones it reports as not read.");
        Assert.AreEqual(0, page.FailedFiles, "No synthetic file fails to read.");
        Assert.AreEqual(
            DedupTrialAssets.ExpectedReadBytes,
            page.ReadBytes,
            "The analysis reads exactly the padding and both copies of the pair, and no other bytes.");
        Assert.AreEqual(
            DedupTrialAssets.ExpectedSkippedBytes,
            DedupTrialAssets.ExpectedWrittenBytes - page.ReadBytes,
            "Everything the trial wrote that the analysis did not read is accounted for by its own counters.");
        Assert.AreNotEqual(string.Empty, page.PlanStatus, "The plan states its own status.");
        Assert.IsNotEmpty(page.Groups, "The duplicate pair must appear as a group with both members.");
        Assert.HasCount(2, page.Groups[0].Members());

        // The lease was really renewed while the worker held it. A durable task's update time only moves
        // when its state changes or its lease is renewed, and between the claim that started this attempt
        // and the finish that ended it nothing else about the task changed — so a task whose own update
        // time is later than the time it was enqueued by more than one heartbeat interval can only have
        // been renewed by the worker's monitor while it was reading the source. The synthetic source is
        // deliberately larger than one interval's worth of hashing, so this is not a timing coincidence.
        var held = analyzed.UpdatedAt - analyzed.CreatedAt;
        Assert.IsGreaterThan(
            DedupTrialDriver.HeartbeatInterval,
            held,
            $"The attempt held its lease for {held.TotalSeconds:F1}s while reading {page.ReadBytes} bytes, which must exceed one heartbeat interval.");

        // 4. revalidate: a recheck of the current version is accepted as its own durable task and reports
        //    its own outcome, so the page can tell "queued" from "finished" instead of guessing.
        var accepted = await dedup.RevalidateAsync(library, first.TaskId, page.PlanDigest);
        Assert.AreEqual("pending", accepted.State);
        Assert.IsFalse(accepted.Completed);
        var rechecked = await dedup.WaitForRecheckAsync(library, first.TaskId, accepted.RecheckTaskId);
        Assert.AreEqual("completed", rechecked.State);
        Assert.IsTrue(rechecked.Completed, rechecked.FailureCode ?? "A recheck of an unchanged library must complete.");
        Assert.IsTrue(rechecked.PlanStillCurrent, "Nothing changed on disk, so the plan is still current.");

        // 5. export: the document states the version it exports and that it grants no file operation.
        var exported = await dedup.ExportAsync(library, first.TaskId, rechecked.AnalysisVersion, rechecked.RecheckPlanDigest);
        Assert.AreEqual(rechecked.AnalysisVersion, exported.AnalysisVersion);
        Assert.IsFalse(exported.GrantsFileOperation, "An exported plan is never an execution instruction.");
        Assert.IsGreaterThan(0, exported.GroupCount);
        var mismatched = await dedup.ExportWithVersionAsync(library, first.TaskId, "00000000-0000-0000-0000-000000000000:9");
        Assert.AreEqual(409, mismatched.Status, "An export bound to another version is refused, not re-bound.");
        Assert.AreEqual("dedup_version_conflict", mismatched.Code);

        // 6. cancel: a real cancellation request against the very task the worker is holding. The job is
        //    addressed the way the page addresses it — by the library and the operation key the start used,
        //    which is what makes a retry replay one job instead of creating a second one. The attempt is
        //    confirmed claimed and still running before the request, so what follows is the cancellation of
        //    work in progress rather than the cancellation of a job that had already finished.
        var operation = Guid.NewGuid();
        var second = await dedup.StartAsync(library, operation);
        Assert.IsTrue(
            second.State is "queued" or "leased",
            $"A second analysis must be accepted while the first one is finished, observed {second.State}.");
        Assert.IsFalse(second.ReportAvailable, "A second analysis has no report while it runs.");
        var running = await leases.WaitForClaimAsync(second.TaskId);
        Assert.AreEqual("leased", running.State, "The attempt that is about to be cancelled is really running.");
        Assert.IsNull(running.CancellationRequestedAt, "Nothing had asked for this attempt to stop yet.");
        var cancelled = await dedup.CancelAsync(library, operation);
        Assert.IsTrue(
            cancelled.CancellationRequested,
            $"The cancellation must be recorded on the durable task, observed {cancelled.State}.");
        var settled = await dedup.WaitForTerminalAsync(library, second.TaskId);
        Assert.AreEqual(
            "cancelled",
            settled.State,
            "Cancelling a running analysis ends it as cancelled; a run that finished anyway would mean the request arrived too late to be one.");
        Assert.IsTrue(settled.CancellationRequested, "The durable task keeps the cancellation it recorded.");
        Assert.IsFalse(
            settled.ReportAvailable,
            "A cancelled analysis reports no plan: cancellation is not a result.");
        var cancelledRow = await leases.ReadAsync(second.TaskId);
        Assert.IsNotNull(cancelledRow, "The cancelled attempt is still a durable task.");
        Assert.AreEqual("cancelled", cancelledRow.State);
        Assert.IsNotNull(cancelledRow.CancellationRequestedAt, "The store recorded when cancellation was asked for.");
        Assert.AreEqual(
            second.TaskId,
            (await dedup.JobAsync(library, second.TaskId)).TaskId,
            "A cancelled job is still addressable by the task id the caller was given.");

        // The first analysis is still the library's readable report at this point: the second attempt was
        // cancelled before it produced anything, so what the restart below is about to lose is a report
        // that really was readable.
        var beforeRestart = await dedup.JobAsync(library, first.TaskId);
        Assert.AreEqual("succeeded", beforeRestart.State, "The first analysis is still the library's finished job.");
        Assert.IsTrue(beforeRestart.ReportAvailable, "Its report is readable before the restart.");
        var readable = await dedup.ResultsAsync(library, first.TaskId);
        Assert.IsTrue(readable.ReportAvailable, "Its findings are readable before the restart.");
        Assert.IsGreaterThan(0, readable.Total, "The report that is about to be lost really had findings.");

        // The task survives a restart because TaskHealth owns it; the report does not, because retention is
        // bounded and in process. The job must say so rather than read as an analysis that found nothing.
        await host.RestartAsync();
        var afterRestart = await dedup.JobAsync(library, first.TaskId);
        Assert.AreEqual(first.TaskId, afterRestart.TaskId, "A durable task outlives the process that ran it.");
        Assert.AreEqual(
            beforeRestart.State,
            afterRestart.State,
            "A restart must not change what the durable task already recorded about itself.");
        Assert.IsFalse(
            afterRestart.ReportAvailable,
            "Retained reports are bounded and in memory, so after a restart the report is gone and the job must say so.");

        // And an operation that addresses that report states the same thing rather than answering about
        // nothing: a report that is not readable is never presented as an analysis that found nothing.
        var gone = await dedup.ResultsAsync(library, first.TaskId);
        Assert.IsFalse(gone.ReportAvailable, "A report that is gone is not readable.");
        Assert.IsEmpty(gone.Groups);
        Assert.AreEqual(0, gone.Total);
        Assert.AreEqual("dedup_report_not_retained", gone.SummaryFailureCode);
        var unreadable = await dedup.RevalidateRefusalAsync(library, first.TaskId);
        Assert.AreEqual(404, unreadable.Status);
        Assert.AreEqual("dedup_report_not_retained", unreadable.Code);

        // The task the restart found is the same one, and the cancellation it recorded is still there.
        var settledAfterRestart = await dedup.JobAsync(library, second.TaskId);
        Assert.AreEqual("cancelled", settledAfterRestart.State, "A restart does not undo a cancellation.");
        Assert.IsFalse(settledAfterRestart.ReportAvailable, "The cancelled attempt still has no report.");

        // And the whole workbench in a real browser. The HTTP driver above states the wire contract; this
        // states that the page an administrator actually uses reaches the same server, drives the same six
        // operations and reports the same facts — with nothing mocked between them.
        var secondLibrary = await dedup.RegisterAsync(assets.BrowserLibraryRoot, "精确查重浏览器验证");
        var browser = await DedupTrialBrowser.RunAsync(
            host, settings, library, secondLibrary, assets.LibraryRoot, assets.BrowserLibraryRoot);
        foreach (var called in new[] { "start", "status", "results", "cancel", "revalidate", "export" })
        {
            Assert.Contains(
                $"/assetlink/v1/dedup/{called}",
                browser.Operations,
                $"The page must really call {called} against the host.");
        }

        Assert.IsEmpty(assets.Unchanged());
    }
}

/// <summary>
/// The synthetic source of the dedup trial. It is built in the run's owned runtime directory, contains one
/// real byte-identical pair plus files that are unique, and is asserted unchanged at the end: this
/// workbench only ever reads.
/// </summary>
internal sealed class DedupTrialAssets
{
    /// <summary>
    /// How many padding files the trial writes. Enough that reading them outlives several heartbeat
    /// intervals on this machine's disk, while every one of them stays under the per-file ceiling the
    /// analysis is given — a larger single file would be reported as too large rather than read.
    /// </summary>
    private const int PaddingParts = 32;

    /// <summary>
    /// Each padding file's size, strictly under the 64 MiB per-file ceiling the analysis is given: a file
    /// at the ceiling exactly would be reported as too large instead of read. The total is deliberately
    /// larger than this machine's page cache for the trial's runtime root, because an analyser that read
    /// it all from cache would finish in about a tenth of one heartbeat interval, and a trial could then
    /// never observe that a running attempt's lease is really renewed.
    /// </summary>
    private const int PaddingPartBytes = 63 * 1024 * 1024;

    /// <summary>How many files the trial writes in total: the pair, the two unique notes and the padding.</summary>
    public const int ExpectedFileCount = PaddingParts + 4;

    /// <summary>
    /// The bytes the analyser has to have read: the padding and both copies of the duplicate pair. The two
    /// small notes are reported in its own skipped/not-read counters instead of its read count, so they are
    /// asserted there rather than here.
    /// </summary>
    public static long ExpectedReadBytes =>
        ((long)PaddingParts * PaddingPartBytes) + (2 * DuplicateBytes().Length);

    /// <summary>The bytes it reports as deliberately not read, which is the two small notes and nothing else.</summary>
    public static long ExpectedSkippedBytes => Readme.Length + Notes.Length;

    /// <summary>Every byte the trial wrote, whether the analyser read it or reported it as not read.</summary>
    public static long ExpectedWrittenBytes => ExpectedReadBytes + ExpectedSkippedBytes;

    private static readonly byte[] Readme = Encoding.UTF8.GetBytes("A file with no duplicate anywhere in this source.");

    private static readonly byte[] Notes = Encoding.UTF8.GetBytes("Isolated international path fixture for the workbench.");

    private readonly Dictionary<string, (string Hash, DateTime Modified)> originals = [];

    public DedupTrialAssets(string runtimeRoot)
    {
        // Inside the configured storage source's allowed root, which is <runtime>/assets: the trial
        // authorizes exactly that root, so a source outside it is refused before a byte is read — which
        // is the behaviour this fixture must not work around.
        var assets = Directory.CreateDirectory(Path.Combine(runtimeRoot, "assets")).FullName;
        LibraryRoot = Populate(Path.Combine(assets, "dedup-library"));
        // A second registration of its own, so the browser phase has a library whose start is a genuinely
        // new durable task. Registration refuses a root that overlaps another library's, so this one is a
        // sibling directory with its own copy rather than a subdirectory of the first.
        BrowserLibraryRoot = Populate(Path.Combine(assets, "dedup-browser-library"));
    }

    public string LibraryRoot { get; }

    /// <summary>The root of the second registration, which only the browser phase analyzes.</summary>
    public string BrowserLibraryRoot { get; }

    /// <summary>Adds one more unique file, so a second analysis sees a source that really changed.</summary>
    public void AddUnique(string relative, string content) => Write(LibraryRoot, relative, Encoding.UTF8.GetBytes(content));

    /// <summary>Every path whose bytes or modification time no longer match what the trial created.</summary>
    public IReadOnlyList<string> Unchanged() => [.. originals
        .Where(entry => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(entry.Key)))
                != entry.Value.Hash
            || File.GetLastWriteTimeUtc(entry.Key) != entry.Value.Modified)
        .Select(entry => entry.Key)];

    /// <summary>
    /// Fills one root with the trial's source. Both registrations are filled the same way, so whichever one
    /// is analyzed has the same one real duplicate pair, the same two files that are deliberately not read
    /// and the same unique padding.
    /// </summary>
    private string Populate(string root)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "set-a"));
        Directory.CreateDirectory(Path.Combine(root, "set-b"));
        // One byte-identical pair, written separately so neither file is a link or a copy on write.
        Write(root, "set-a/original.bin", DuplicateBytes());
        Write(root, "set-b/copy.bin", DuplicateBytes());
        Write(root, "unique/readme.txt", Readme);
        Write(root, "unique/notes_中文.txt", Notes);
        // Padding, so the analysis is long enough to outlive several heartbeat intervals. It is written as
        // several files because a single file over the per-file ceiling would be reported as too large
        // instead of read, and it is unique content, so it can never be mistaken for a duplicate.
        for (var part = 0; part < PaddingParts; part++)
        {
            Write(root, $"unique/padding-{part:D2}.bin", PaddingBytes(part));
        }

        return root;
    }

    private static byte[] DuplicateBytes()
    {
        var bytes = new byte[2048];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        return bytes;
    }

    /// <summary>
    /// A unique file big enough that hashing all of them outlives one lease heartbeat interval, so the
    /// worker's own renewal is observable from outside the process. They are deterministic rather than
    /// random, so a rerun reads the same bytes, and their content cannot collide with the duplicate pair.
    /// </summary>
    private static byte[] PaddingBytes(int part)
    {
        var bytes = new byte[PaddingPartBytes];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((index * 31 + (part * 97) + 7) % 251);
        }

        return bytes;
    }

    private void Write(string root, string relative, byte[] content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        originals[path] = (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)), File.GetLastWriteTimeUtc(path));
    }
}
