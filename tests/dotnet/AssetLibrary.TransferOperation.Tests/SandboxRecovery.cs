using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal enum SandboxRecoveryOutcome
{
    Complete = 0,
    Conflict = 1,
}

internal sealed record SandboxRecoveryResult(
    SandboxRecoveryOutcome Outcome,
    PayloadFacts? VerifiedDestination);

internal sealed class SandboxRecovery(
    SandboxPathBoundary boundary,
    SandboxJournalStore journalStore,
    SandboxFileHash fileHash)
{
    public async ValueTask<SandboxRecoveryResult> RecoverAsync(
        OperationPlanId planId,
        OperationItemId itemId,
        CancellationToken cancellationToken)
    {
        var record = await journalStore.ReadAsync(
            planId,
            itemId,
            cancellationToken).ConfigureAwait(false);
        var expected = SandboxJournalStore.Expected(record);
        return record.Operation switch
        {
            PhysicalOperationKind.Copy => await SandboxCopyMoveRecovery.RunAsync(
                boundary,
                journalStore,
                fileHash,
                record,
                expected,
                moveSource: false,
                cancellationToken).ConfigureAwait(false),
            PhysicalOperationKind.Move => await SandboxCopyMoveRecovery.RunAsync(
                boundary,
                journalStore,
                fileHash,
                record,
                expected,
                moveSource: true,
                cancellationToken).ConfigureAwait(false),
            PhysicalOperationKind.Rename or PhysicalOperationKind.Restore =>
                await SandboxRenameRestoreRecovery.RunAsync(
                    boundary,
                    journalStore,
                    fileHash,
                    record,
                    expected,
                    cancellationToken).ConfigureAwait(false),
            PhysicalOperationKind.Trash => await SandboxTrashRecovery.RunAsync(
                boundary,
                journalStore,
                fileHash,
                record,
                expected,
                cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("The recovery journal operation is unknown."),
        };
    }
}

internal static class SandboxRecoveryFacts
{
    public static async ValueTask<PayloadFacts?> ReadAsync(
        SandboxPathBoundary boundary,
        SandboxFileHash fileHash,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var path = boundary.ResolveRelative(relativePath);
        return File.Exists(path)
            ? await fileHash.ComputeAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public static async ValueTask<SandboxRecoveryResult> FinishAsync(
        SandboxJournalStore store,
        SandboxJournalRecord record,
        SandboxRecoveryOutcome outcome,
        PayloadFacts? facts,
        CancellationToken cancellationToken)
    {
        var state = outcome == SandboxRecoveryOutcome.Complete
            ? SandboxJournalState.Complete
            : SandboxJournalState.Conflict;
        await store.WriteAsync(record with { State = state }, cancellationToken).ConfigureAwait(false);
        return new(outcome, outcome == SandboxRecoveryOutcome.Complete ? facts : null);
    }
}

internal static class SandboxCopyMoveRecovery
{
    public static async ValueTask<SandboxRecoveryResult> RunAsync(
        SandboxPathBoundary boundary,
        SandboxJournalStore store,
        SandboxFileHash fileHash,
        SandboxJournalRecord record,
        PayloadFacts expected,
        bool moveSource,
        CancellationToken cancellationToken)
    {
        var targetRelative = record.TargetRelative
            ?? throw new InvalidOperationException("Copy recovery requires a target.");
        var stageRelative = record.StageRelative
            ?? throw new InvalidOperationException("Copy recovery requires a stage.");
        var target = boundary.ResolveRelative(targetRelative);
        var stage = boundary.ResolveRelative(stageRelative);
        var targetFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            targetRelative,
            cancellationToken).ConfigureAwait(false);
        var stageFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            stageRelative,
            cancellationToken).ConfigureAwait(false);
        if (targetFacts == expected && stageFacts is null)
        {
            if (moveSource)
            {
                return await CompleteMoveAsync(
                    boundary,
                    store,
                    fileHash,
                    record,
                    expected,
                    targetFacts,
                    cancellationToken).ConfigureAwait(false);
            }

            var copySourceFacts = await SandboxRecoveryFacts.ReadAsync(
                boundary,
                fileHash,
                record.SourceRelative,
                cancellationToken).ConfigureAwait(false);
            return copySourceFacts == expected
                ? await SandboxRecoveryFacts.FinishAsync(
                    store,
                    record,
                    SandboxRecoveryOutcome.Complete,
                    targetFacts,
                    cancellationToken).ConfigureAwait(false)
                : await ConflictAsync(store, record, cancellationToken).ConfigureAwait(false);
        }

        if (targetFacts is not null || stageFacts != expected)
        {
            return await ConflictAsync(store, record, cancellationToken).ConfigureAwait(false);
        }

        var sourceFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            record.SourceRelative,
            cancellationToken).ConfigureAwait(false);
        if (sourceFacts != expected
            || !SandboxFilePrimitives.MoveNoReplace(boundary, stage, target))
        {
            return await ConflictAsync(store, record, cancellationToken).ConfigureAwait(false);
        }

        targetFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            targetRelative,
            cancellationToken).ConfigureAwait(false);
        if (targetFacts != expected)
        {
            return await ConflictAsync(store, record, cancellationToken).ConfigureAwait(false);
        }

        return moveSource
            ? await CompleteMoveAsync(
                boundary,
                store,
                fileHash,
                record,
                expected,
                targetFacts,
                cancellationToken).ConfigureAwait(false)
            : await SandboxRecoveryFacts.FinishAsync(
                store,
                record,
                SandboxRecoveryOutcome.Complete,
                targetFacts,
                cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<SandboxRecoveryResult> CompleteMoveAsync(
        SandboxPathBoundary boundary,
        SandboxJournalStore store,
        SandboxFileHash fileHash,
        SandboxJournalRecord record,
        PayloadFacts expected,
        PayloadFacts targetFacts,
        CancellationToken cancellationToken)
    {
        var source = boundary.ResolveRelative(record.SourceRelative);
        var trash = boundary.ResolveRelative(record.TrashRelative);
        var sourceFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            record.SourceRelative,
            cancellationToken).ConfigureAwait(false);
        var trashFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            record.TrashRelative,
            cancellationToken).ConfigureAwait(false);
        if (sourceFacts == expected && trashFacts is null)
        {
            if (!SandboxFilePrimitives.MoveNoReplace(boundary, source, trash))
            {
                return await ConflictAsync(store, record, cancellationToken).ConfigureAwait(false);
            }

            sourceFacts = null;
            trashFacts = await SandboxRecoveryFacts.ReadAsync(
                boundary,
                fileHash,
                record.TrashRelative,
                cancellationToken).ConfigureAwait(false);
        }

        return sourceFacts is null && trashFacts == expected
            ? await SandboxRecoveryFacts.FinishAsync(
                store,
                record,
                SandboxRecoveryOutcome.Complete,
                targetFacts,
                cancellationToken).ConfigureAwait(false)
            : await ConflictAsync(store, record, cancellationToken).ConfigureAwait(false);
    }

    private static ValueTask<SandboxRecoveryResult> ConflictAsync(
        SandboxJournalStore store,
        SandboxJournalRecord record,
        CancellationToken cancellationToken) =>
        SandboxRecoveryFacts.FinishAsync(
            store,
            record,
            SandboxRecoveryOutcome.Conflict,
            null,
            cancellationToken);
}

internal static class SandboxRenameRestoreRecovery
{
    public static async ValueTask<SandboxRecoveryResult> RunAsync(
        SandboxPathBoundary boundary,
        SandboxJournalStore store,
        SandboxFileHash fileHash,
        SandboxJournalRecord record,
        PayloadFacts expected,
        CancellationToken cancellationToken)
    {
        var targetRelative = record.TargetRelative
            ?? throw new InvalidOperationException("Rename recovery requires a target.");
        var sourceFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            record.SourceRelative,
            cancellationToken).ConfigureAwait(false);
        var targetFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            targetRelative,
            cancellationToken).ConfigureAwait(false);
        if (sourceFacts == expected && targetFacts is null)
        {
            var source = boundary.ResolveRelative(record.SourceRelative);
            var target = boundary.ResolveRelative(targetRelative);
            if (SandboxFilePrimitives.MoveNoReplace(boundary, source, target))
            {
                sourceFacts = null;
                targetFacts = await SandboxRecoveryFacts.ReadAsync(
                    boundary,
                    fileHash,
                    targetRelative,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var outcome = sourceFacts is null && targetFacts == expected
            ? SandboxRecoveryOutcome.Complete
            : SandboxRecoveryOutcome.Conflict;
        return await SandboxRecoveryFacts.FinishAsync(
            store,
            record,
            outcome,
            targetFacts,
            cancellationToken).ConfigureAwait(false);
    }
}

internal static class SandboxTrashRecovery
{
    public static async ValueTask<SandboxRecoveryResult> RunAsync(
        SandboxPathBoundary boundary,
        SandboxJournalStore store,
        SandboxFileHash fileHash,
        SandboxJournalRecord record,
        PayloadFacts expected,
        CancellationToken cancellationToken)
    {
        var sourceFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            record.SourceRelative,
            cancellationToken).ConfigureAwait(false);
        var trashFacts = await SandboxRecoveryFacts.ReadAsync(
            boundary,
            fileHash,
            record.TrashRelative,
            cancellationToken).ConfigureAwait(false);
        if (sourceFacts == expected && trashFacts is null)
        {
            var source = boundary.ResolveRelative(record.SourceRelative);
            var trash = boundary.ResolveRelative(record.TrashRelative);
            if (SandboxFilePrimitives.MoveNoReplace(boundary, source, trash))
            {
                sourceFacts = null;
                trashFacts = await SandboxRecoveryFacts.ReadAsync(
                    boundary,
                    fileHash,
                    record.TrashRelative,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var outcome = sourceFacts is null && trashFacts == expected
            ? SandboxRecoveryOutcome.Complete
            : SandboxRecoveryOutcome.Conflict;
        return await SandboxRecoveryFacts.FinishAsync(
            store,
            record,
            outcome,
            trashFacts,
            cancellationToken).ConfigureAwait(false);
    }
}
