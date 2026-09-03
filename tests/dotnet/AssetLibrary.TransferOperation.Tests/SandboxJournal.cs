using System.Text.Json;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal enum SandboxJournalState
{
    Prepared = 0,
    Staged = 1,
    TargetCommitted = 2,
    SourceTrashed = 3,
    Complete = 4,
    Conflict = 5,
}

internal sealed record SandboxJournalRecord(
    Guid PlanId,
    Guid ItemId,
    PhysicalOperationKind Operation,
    string SourceRelative,
    string? TargetRelative,
    string? StageRelative,
    string TrashRelative,
    long ExpectedLength,
    string ExpectedSha256,
    SandboxJournalState State);

internal sealed class SandboxJournalStore(SandboxPathBoundary boundary)
{
    private const long MaximumJournalBytes = 16 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public string JournalPath(OperationPlanId planId, OperationItemId itemId) =>
        boundary.ResolveInternal(
            "journal",
            planId.Value.ToString("N"),
            $"{itemId.Value:N}.json");

    public async ValueTask WriteAsync(
        SandboxJournalRecord record,
        CancellationToken cancellationToken)
    {
        Validate(record);
        var journal = JournalPath(
            new OperationPlanId(record.PlanId),
            new OperationItemId(record.ItemId));
        SandboxFilePrimitives.EnsureParent(boundary, journal);
        var temporary = boundary.ResolveInternal(
            "journal",
            record.PlanId.ToString("N"),
            $"{record.ItemId:N}.{Guid.NewGuid():N}.partial");
        await using (var stream = new FileStream(
            temporary,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                record,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        boundary.Revalidate(temporary);
        boundary.Revalidate(journal);
        File.Move(temporary, journal, overwrite: true);
    }

    public async ValueTask<SandboxJournalRecord> ReadAsync(
        OperationPlanId planId,
        OperationItemId itemId,
        CancellationToken cancellationToken)
    {
        var path = JournalPath(planId, itemId);
        boundary.Revalidate(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaximumJournalBytes)
        {
            throw new InvalidOperationException("The sandbox recovery journal is missing or oversized.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var record = await JsonSerializer.DeserializeAsync<SandboxJournalRecord>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The sandbox recovery journal is empty.");
        if (record.PlanId != planId.Value || record.ItemId != itemId.Value)
        {
            throw new InvalidOperationException("The sandbox recovery journal identity changed.");
        }

        Validate(record);
        return record;
    }

    public static PayloadFacts Expected(SandboxJournalRecord record) =>
        new(record.ExpectedLength, new Sha256Digest(record.ExpectedSha256));

    private void Validate(SandboxJournalRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.PlanId == Guid.Empty
            || record.ItemId == Guid.Empty
            || !Enum.IsDefined(record.Operation)
            || !Enum.IsDefined(record.State)
            || record.ExpectedLength < 0)
        {
            throw new InvalidOperationException("The sandbox recovery journal is invalid.");
        }

        _ = new Sha256Digest(record.ExpectedSha256);
        boundary.ResolveRelative(record.SourceRelative);
        if (record.TargetRelative is not null)
        {
            boundary.ResolveRelative(record.TargetRelative);
        }

        if (record.StageRelative is not null)
        {
            boundary.ResolveRelative(record.StageRelative);
        }

        boundary.ResolveRelative(record.TrashRelative);
        var expectedStage = record.Operation is PhysicalOperationKind.Copy
            or PhysicalOperationKind.Move
            ? boundary.ToRelative(
                boundary.ResolveInternal(
                    "stage",
                    record.PlanId.ToString("N"),
                    record.ItemId.ToString("N"),
                    "payload.partial"))
            : null;
        var expectedTrash = boundary.ToRelative(
            boundary.ResolveInternal(
                "trash",
                record.PlanId.ToString("N"),
                record.ItemId.ToString("N"),
                "payload"));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(record.StageRelative, expectedStage, comparison)
            || !string.Equals(record.TrashRelative, expectedTrash, comparison))
        {
            throw new InvalidOperationException(
                "The sandbox recovery journal internal paths changed.");
        }
    }
}
