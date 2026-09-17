using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Position of one page inside one report version. A cursor is only accepted together with the exact
/// report version it was minted for, so a page read before a recheck cannot be spliced into a newer
/// result set.
/// </summary>
public sealed record DedupCursor(
    DedupReportKey Key,
    string PlanDigest,
    string? GroupKey,
    DedupFindingKind? Kind,
    int Offset);

/// <summary>
/// Signs and verifies result cursors. The secret belongs to one retained report, so a cursor becomes
/// worthless the moment that report is replaced: an obsolete page is refused instead of being
/// answered from different evidence.
/// </summary>
public static class DedupCursorPolicy
{
    private const int MaximumCursorLength = 1024;
    private const int SecretBytes = 32;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public static string Encode(DedupCursor cursor, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length != SecretBytes)
        {
            throw new ArgumentException("A cursor secret has a fixed length.", nameof(secret));
        }

        if (cursor.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cursor), "A cursor offset cannot be negative.");
        }

        var payload = string.Join(
            '\n',
            cursor.Key.TaskId.ToString("D", CultureInfo.InvariantCulture),
            cursor.Key.Generation.ToString(CultureInfo.InvariantCulture),
            cursor.PlanDigest,
            cursor.GroupKey ?? string.Empty,
            cursor.Kind is { } kind ? ((int)kind).ToString(CultureInfo.InvariantCulture) : string.Empty,
            cursor.Offset.ToString(CultureInfo.InvariantCulture));
        return Base64Url(Encoding.UTF8.GetBytes($"{payload}\n{Sign(payload, secret)}"));
    }

    public static DedupCursor Decode(
        string text,
        DedupReportKey expectedKey,
        string expectedDigest,
        Func<DedupReportKey, byte[]?> secretOf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(secretOf);
        if (text.Length > MaximumCursorLength)
        {
            throw new InvalidDedupCursorException();
        }

        string[] fields;
        try
        {
            fields = Encoding.UTF8.GetString(FromBase64Url(text)).Split('\n');
        }
        catch (Exception error) when (error is FormatException or DecoderFallbackException)
        {
            throw new InvalidDedupCursorException();
        }

        if (fields.Length != 7
            || !Guid.TryParseExact(fields[0], "D", out var taskId)
            || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
            || !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
            || generation <= 0
            || offset < 0
            || fields[3].Length > 4096
            || !string.Equals(fields[2], expectedDigest, StringComparison.Ordinal))
        {
            throw new InvalidDedupCursorException();
        }

        DedupFindingKind? kind = null;
        if (fields[4].Length > 0)
        {
            if (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var kindValue)
                || !Enum.IsDefined((DedupFindingKind)kindValue))
            {
                throw new InvalidDedupCursorException();
            }

            kind = (DedupFindingKind)kindValue;
        }

        var key = new DedupReportKey(taskId, generation);
        var secret = secretOf(key);
        if (secret is null)
        {
            throw new InvalidDedupCursorException();
        }

        // The payload is rebuilt from the parsed fields, so the signature covers the value this
        // code actually uses rather than the raw text a caller supplied.
        var payload = string.Join(
            '\n',
            key.TaskId.ToString("D", CultureInfo.InvariantCulture),
            key.Generation.ToString(CultureInfo.InvariantCulture),
            fields[2],
            fields[3],
            fields[4],
            offset.ToString(CultureInfo.InvariantCulture));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Sign(payload, secret)),
                Encoding.ASCII.GetBytes(fields[6])))
        {
            throw new InvalidDedupCursorException();
        }

        var cursor = new DedupCursor(key, fields[2], fields[3].Length == 0 ? null : fields[3], kind, offset);
        RequireCurrent(cursor, expectedKey, expectedDigest);
        return cursor;
    }

    /// <summary>
    /// A cursor is refused once it no longer describes the version it was minted for. It is not an
    /// error the caller may ignore: answering anyway would present older evidence as current.
    /// </summary>
    public static void RequireCurrent(DedupCursor cursor, DedupReportKey currentKey, string currentDigest)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        if (cursor.Key != currentKey || !string.Equals(cursor.PlanDigest, currentDigest, StringComparison.Ordinal))
        {
            throw new InvalidDedupCursorException();
        }
    }

    private static string Sign(string payload, byte[] secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(payload)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            0 => padded,
            _ => throw new FormatException("The cursor is not valid base64url."),
        };
        return Convert.FromBase64String(padded);
    }
}

public sealed class InvalidDedupCursorException()
    : ArgumentException("结果游标无效或已过期，请重新读取结果。");

/// <summary>
/// Bounded in-process retention of finished reports. AssetIdentity owns this type because a reported
/// finding is its own read model; TaskHealth still owns the durable job, its lease fence and its
/// cancellation record. Nothing here grants a file operation.
/// </summary>
public sealed class DedupReportRegistry
{
    private readonly int maximumReports;
    private readonly Lock gate = new();
    private readonly Dictionary<DedupReportKey, Entry> reports = [];
    private readonly Dictionary<Guid, DedupReportKey> latest = [];
    private readonly Dictionary<Guid, DedupAnalysisLimits> limitsOf = [];
    private readonly Dictionary<Guid, DedupRecheckRun> rechecks = [];
    private long published;

    public DedupReportRegistry(int maximumReports)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReports);
        this.maximumReports = maximumReports;
    }

    /// <summary>
    /// Files what one recheck found, keyed by the recheck's own durable task. A recheck runs in the
    /// background, so its outcome has to live somewhere the next status call can read; keeping it beside
    /// the reports means it is bounded by the same retention as the evidence it describes.
    /// </summary>
    public void RecordRecheck(Guid recheckTaskId, DedupRecheckRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (gate)
        {
            rechecks[recheckTaskId] = run;
        }
    }

    public DedupRecheckRun? RecheckOf(Guid recheckTaskId)
    {
        lock (gate)
        {
            return rechecks.GetValueOrDefault(recheckTaskId);
        }
    }

    public static DedupReportRegistry Default { get; } = new(DedupJobContractText.MaximumRetainedReports);

    public DedupReportKey Publish(DedupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (gate)
        {
            var key = new DedupReportKey(report.TaskId, ++published);
            reports[key] = new Entry(report, DedupCursorPolicy.NewSecret(), null);
            latest[report.LibraryId.Value] = key;
            while (reports.Count > maximumReports)
            {
                EvictOldest();
            }

            return key;
        }
    }

    /// <summary>
    /// Records the ceilings one job was accepted under. They are request parameters rather than
    /// observed findings, so they are kept per job and stated in the page and in an export.
    /// </summary>
    public void RecordLimits(Guid taskId, DedupAnalysisLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        lock (gate)
        {
            limitsOf[taskId] = limits;
        }
    }

    public DedupAnalysisLimits? LimitsOf(Guid taskId)
    {
        lock (gate)
        {
            return limitsOf.GetValueOrDefault(taskId);
        }
    }

    public DedupRecheckEvidence? EvidenceOf(DedupReportKey key)
    {
        lock (gate)
        {
            return reports.TryGetValue(key, out var entry) ? entry.Evidence : null;
        }
    }

    public bool TryGet(DedupReportKey key, out DedupReport report)
    {
        lock (gate)
        {
            if (reports.TryGetValue(key, out var entry))
            {
                report = entry.Report;
                return true;
            }
        }

        report = null!;
        return false;
    }

    /// <summary>The newest retained report of one library, so a reload can resume a review.</summary>
    public bool TryLatest(Guid libraryId, out DedupReportKey key, out DedupReport report)
    {
        lock (gate)
        {
            if (latest.TryGetValue(libraryId, out var found) && reports.TryGetValue(found, out var entry))
            {
                key = found;
                report = entry.Report;
                return true;
            }
        }

        key = default;
        report = null!;
        return false;
    }

    /// <summary>
    /// Files a later version of one report against the version it was produced from, in one step. The
    /// comparison and the write happen under the same lock because they are one decision: keeping an old
    /// version readable is normal retention, so "the key is still there" cannot prove it is still the
    /// version a reader would see, and a check made outside this lock can be overtaken by an analysis that
    /// publishes in between. Answering <see cref="FileOutcome.Superseded"/> therefore covers both a dropped
    /// version and a library that has moved on to a newer one.
    /// </summary>
    public FileOutcome FileIfCurrent(
        Guid libraryId,
        DedupReportKey expected,
        string expectedDigest,
        DedupReport next,
        DedupRecheckEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(evidence);
        lock (gate)
        {
            if (!reports.TryGetValue(expected, out var entry))
            {
                return FileOutcome.Superseded;
            }

            if (!string.Equals(entry.Report.PlanDigest, expectedDigest, StringComparison.Ordinal)
                || !latest.TryGetValue(libraryId, out var current)
                || current != expected)
            {
                return FileOutcome.Superseded;
            }

            var key = new DedupReportKey(next.TaskId, ++published);
            reports[key] = new Entry(next, DedupCursorPolicy.NewSecret(), evidence);
            latest[libraryId] = key;
            while (reports.Count > maximumReports)
            {
                EvictOldest();
            }

            return new FileOutcome(true, key);
        }
    }

    /// <summary>
    /// Files a recheck verdict that produced no new plan version, so the evidence lands beside the version
    /// it verified under the same comparison rule.
    /// </summary>
    public FileOutcome StoreEvidenceIfCurrent(
        Guid libraryId,
        DedupReportKey expected,
        string expectedDigest,
        DedupRecheckEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        lock (gate)
        {
            if (!reports.TryGetValue(expected, out var entry)
                || !string.Equals(entry.Report.PlanDigest, expectedDigest, StringComparison.Ordinal)
                || !latest.TryGetValue(libraryId, out var current)
                || current != expected)
            {
                return FileOutcome.Superseded;
            }

            reports[expected] = entry with { Evidence = evidence };
            return new FileOutcome(false, expected);
        }
    }

    public byte[]? SecretOf(DedupReportKey key)
    {
        lock (gate)
        {
            return reports.TryGetValue(key, out var entry) ? entry.Secret : null;
        }
    }

    public void Discard(DedupReportKey key)
    {
        lock (gate)
        {
            if (!reports.Remove(key, out var entry))
            {
                return;
            }

            if (latest.TryGetValue(entry.Report.LibraryId.Value, out var current) && current == key)
            {
                latest.Remove(entry.Report.LibraryId.Value);
            }
        }
    }

    /// <summary>
    /// A lease that lost its fence must not leave a reviewable report behind: the retry publishes its
    /// own version, and the abandoned one is dropped so it cannot be read as the current evidence.
    /// </summary>
    public void DiscardTask(Guid taskId)
    {
        lock (gate)
        {
            foreach (var key in reports.Keys.Where(candidate => candidate.TaskId == taskId).ToArray())
            {
                reports.Remove(key);
            }

            foreach (var library in latest.Where(pair => pair.Value.TaskId == taskId).Select(pair => pair.Key).ToArray())
            {
                latest.Remove(library);
            }
        }
    }

    private void EvictOldest()
    {
        var oldest = reports.Keys.OrderBy(candidate => candidate.Generation).First();
        var libraryId = reports[oldest].Report.LibraryId.Value;
        reports.Remove(oldest);
        if (latest.TryGetValue(libraryId, out var current) && current == oldest)
        {
            latest.Remove(libraryId);
        }
    }

    private sealed record Entry(DedupReport Report, byte[] Secret, DedupRecheckEvidence? Evidence);
}

/// <summary>
/// What a compare-and-file did. <see cref="Superseded"/> means the version it was asked about is no longer
/// the one a reader of that library would see, so the caller must report a refusal instead of evidence.
/// </summary>
public readonly record struct FileOutcome(bool Filed, DedupReportKey Key)
{
    public static FileOutcome Superseded { get; } = new(false, default);
}
