using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Reads one page of one report version. It owns the cursor contract and the section order, so a
/// page can never be mixed with another version's evidence and an unread file is never answered
/// from the duplicate section.
/// </summary>
internal sealed class DedupResultsReader(DedupReportRegistry reports, DedupJobViewFactory views)
{
    /// <summary>
    /// A page of a report this job no longer holds. It is answered with an explicit
    /// <c>ReportAvailable: false</c> instead of an empty section, because an empty duplicate list
    /// would read as "no duplicates found".
    /// </summary>
    public DedupResultsPage NotRetained(DedupPageRequest request, Guid taskId) =>
        new(
            taskId,
            string.Empty,
            request.Kind ?? DedupFindingKind.ByteDuplicateGroup,
            [],
            [],
            0,
            Clamp(request.PageSize),
            0,
            null,
            ReportAvailable: false,
            views.MissingSummary(taskId, default));

    public DedupResultsPage Read(
        DedupPageRequest request,
        Guid taskId,
        DedupReportKey key,
        DedupReport report)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(report);
        var pageSize = Clamp(request.PageSize);
        var cursor = request.Cursor is { Length: > 0 } text
            ? DedupCursorPolicy.Decode(text, key, report.PlanDigest, reports.SecretOf)
            : null;
        var offset = cursor?.Offset ?? 0;

        // A group page is a view of the files that share one evidence key; every other section is a
        // page of whole findings. The two never share one response shape.
        var groupKey = request.GroupKey ?? cursor?.GroupKey;
        if (groupKey is not null)
        {
            return GroupPage(taskId, key, report, groupKey, offset, pageSize);
        }

        var kind = request.Kind ?? cursor?.Kind ?? DedupFindingKind.ByteDuplicateGroup;
        return kind == DedupFindingKind.ByteDuplicateGroup
            ? GroupFindingsPage(taskId, key, report, offset, pageSize)
            : ItemPage(taskId, key, report, kind, offset, pageSize);
    }

    private DedupResultsPage GroupPage(
        Guid taskId,
        DedupReportKey key,
        DedupReport report,
        string groupKey,
        int offset,
        int pageSize)
    {
        var members = report.Groups
            .FirstOrDefault(group => string.Equals(group.GroupKey, groupKey, StringComparison.Ordinal))
            ?.Members ?? throw new ReadOnlyTrialException("dedup_group_not_found");
        var slice = members.Skip(offset).Take(pageSize).ToArray();
        return new DedupResultsPage(
            taskId, key.VersionText, DedupFindingKind.ByteDuplicateGroup, [], slice, offset, pageSize,
            members.Count, Next(key, report, groupKey, null, offset + slice.Length, members.Count), true,
            DedupJobViewFactory.Summary(key, report));
    }

    private DedupResultsPage GroupFindingsPage(
        Guid taskId,
        DedupReportKey key,
        DedupReport report,
        int offset,
        int pageSize)
    {
        var slice = report.Groups.Skip(offset).Take(pageSize).ToArray();
        return new DedupResultsPage(
            taskId, key.VersionText, DedupFindingKind.ByteDuplicateGroup, slice, [], offset, pageSize,
            report.Groups.Count, Next(key, report, null, DedupFindingKind.ByteDuplicateGroup, offset + slice.Length, report.Groups.Count),
            true, DedupJobViewFactory.Summary(key, report));
    }

    private DedupResultsPage ItemPage(
        Guid taskId,
        DedupReportKey key,
        DedupReport report,
        DedupFindingKind kind,
        int offset,
        int pageSize)
    {
        var items = ReportItemReader.Items(report, kind);
        var slice = items.Skip(offset).Take(pageSize).ToArray();
        return new DedupResultsPage(
            taskId, key.VersionText, kind, [], slice, offset, pageSize,
            items.Count, Next(key, report, null, kind, offset + slice.Length, items.Count), true,
            DedupJobViewFactory.Summary(key, report));
    }

    /// <summary>
    /// Mints the cursor for the page after this one. A cursor that cannot be signed is not issued at
    /// all, so a caller either gets a usable continuation or a final page.
    /// </summary>
    private string? Next(
        DedupReportKey key,
        DedupReport report,
        string? groupKey,
        DedupFindingKind? kind,
        int offset,
        int total)
    {
        if (offset >= total || reports.SecretOf(key) is not { } secret)
        {
            return null;
        }

        return DedupCursorPolicy.Encode(new DedupCursor(key, report.PlanDigest, groupKey, kind, offset), secret);
    }

    private static int Clamp(int pageSize) => pageSize switch
    {
        <= 0 => DedupJobContractText.DefaultPageSize,
        > DedupJobContractText.MaximumPageSize => DedupJobContractText.MaximumPageSize,
        _ => pageSize,
    };
}

/// <summary>
/// Maps a finding kind to the report section that answers it. A file with no same-length peer is
/// deliberately not an answerable section on its own: "not compared" must not be read as "unique".
/// </summary>
internal static class ReportItemReader
{
    public static IReadOnlyList<DedupReportItem> Items(DedupReport report, DedupFindingKind kind)
    {
        ArgumentNullException.ThrowIfNull(report);
        return kind switch
        {
            DedupFindingKind.Unverified => report.Unverified,
            DedupFindingKind.Unreadable => report.Unreadable,
            DedupFindingKind.Unique => [.. report.Groups.SelectMany(group => group.Members)],
            DedupFindingKind.ByteDuplicateGroup => [],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
