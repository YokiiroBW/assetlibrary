using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal static class SandboxCopyMoveOperation
{
    public static async ValueTask<OperationPortReceipt> RunAsync(
        SandboxOperationContext context,
        bool moveSource,
        CancellationToken cancellationToken)
    {
        var stage = context.StagePath
            ?? throw new InvalidOperationException("A copy operation requires a stage.");
        var target = context.TargetPath
            ?? throw new InvalidOperationException("A copy operation requires a target.");
        SandboxFilePrimitives.EnsureParent(context.Fixture.Boundary, stage);
        SandboxCopyResult copied;
        try
        {
            copied = await context.StreamCopy.CopyAsync(
                context.SourcePath,
                stage,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SandboxFilePrimitives.DeleteEvidenceFile(context.Fixture.Boundary, stage);
            throw;
        }

        if (copied.Facts != context.Item.ExpectedSource)
        {
            SandboxFilePrimitives.DeleteEvidenceFile(context.Fixture.Boundary, stage);
            return context.Failed(OperationPortOutcome.SourceChanged);
        }

        await context.WriteStateAsync(
            SandboxJournalState.Staged,
            cancellationToken).ConfigureAwait(false);
        context.FaultPlan.Trigger(SandboxFaultPoint.AfterStageWritten);
        var sourceBeforeCommit = await context.FactsIfPresentAsync(
            context.SourcePath,
            cancellationToken).ConfigureAwait(false);
        if (sourceBeforeCommit != context.Item.ExpectedSource)
        {
            return context.Failed(OperationPortOutcome.SourceChanged);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!SandboxFilePrimitives.MoveNoReplace(context.Fixture.Boundary, stage, target))
        {
            return context.Failed(OperationPortOutcome.TargetExists);
        }

        using var finalization = SandboxFinalization.Create(
            context.Request.Deadline,
            context.ExecutionRight.ExpiresAt);
        try
        {
            await context.WriteStateAsync(
                SandboxJournalState.TargetCommitted,
                finalization.Token).ConfigureAwait(false);
            context.FaultPlan.Trigger(SandboxFaultPoint.AfterTargetCommitted);
            var targetFacts = await context.FactsIfPresentAsync(
                target,
                finalization.Token).ConfigureAwait(false);
            if (targetFacts != context.Item.ExpectedSource)
            {
                return context.Failed(OperationPortOutcome.HashMismatch);
            }

            if (moveSource)
            {
                var sourceBeforeTrash = await context.FactsIfPresentAsync(
                    context.SourcePath,
                    finalization.Token).ConfigureAwait(false);
                if (sourceBeforeTrash != context.Item.ExpectedSource)
                {
                    return context.Failed(OperationPortOutcome.SourceChanged);
                }

                context.FaultPlan.Trigger(SandboxFaultPoint.BeforeSourceTrash);
                if (!SandboxFilePrimitives.MoveNoReplace(
                    context.Fixture.Boundary,
                    context.SourcePath,
                    context.TrashPath))
                {
                    return context.Failed(OperationPortOutcome.ManualReviewRequired);
                }

                await context.WriteStateAsync(
                    SandboxJournalState.SourceTrashed,
                    finalization.Token).ConfigureAwait(false);
                context.FaultPlan.Trigger(SandboxFaultPoint.AfterSourceTrash);
                var trashFacts = await context.FactsIfPresentAsync(
                    context.TrashPath,
                    finalization.Token).ConfigureAwait(false);
                if (trashFacts != context.Item.ExpectedSource)
                {
                    return context.Failed(OperationPortOutcome.ManualReviewRequired);
                }
            }

            await context.WriteStateAsync(
                SandboxJournalState.Complete,
                finalization.Token).ConfigureAwait(false);
            return context.Completed(targetFacts);
        }
        catch (OperationCanceledException) when (finalization.IsCancellationRequested)
        {
            return context.Failed(OperationPortOutcome.ManualReviewRequired);
        }
    }
}

internal static class SandboxRenameRestoreOperation
{
    public static async ValueTask<OperationPortReceipt> RunAsync(
        SandboxOperationContext context,
        CancellationToken cancellationToken)
    {
        var target = context.TargetPath
            ?? throw new InvalidOperationException("Rename and restore require a target.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!SandboxFilePrimitives.MoveNoReplace(
            context.Fixture.Boundary,
            context.SourcePath,
            target))
        {
            return context.Failed(OperationPortOutcome.TargetExists);
        }

        using var finalization = SandboxFinalization.Create(
            context.Request.Deadline,
            context.ExecutionRight.ExpiresAt);
        try
        {
            await context.WriteStateAsync(
                SandboxJournalState.TargetCommitted,
                finalization.Token).ConfigureAwait(false);
            context.FaultPlan.Trigger(SandboxFaultPoint.AfterTargetCommitted);
            var targetFacts = await context.FactsIfPresentAsync(
                target,
                finalization.Token).ConfigureAwait(false);
            if (targetFacts != context.Item.ExpectedSource)
            {
                return context.Failed(OperationPortOutcome.HashMismatch);
            }

            await context.WriteStateAsync(
                SandboxJournalState.Complete,
                finalization.Token).ConfigureAwait(false);
            return context.Completed(targetFacts);
        }
        catch (OperationCanceledException) when (finalization.IsCancellationRequested)
        {
            return context.Failed(OperationPortOutcome.ManualReviewRequired);
        }
    }
}

internal static class SandboxTrashOperation
{
    public static async ValueTask<OperationPortReceipt> RunAsync(
        SandboxOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.FaultPlan.Trigger(SandboxFaultPoint.BeforeSourceTrash);
        if (!SandboxFilePrimitives.MoveNoReplace(
            context.Fixture.Boundary,
            context.SourcePath,
            context.TrashPath))
        {
            return context.Failed(OperationPortOutcome.ManualReviewRequired);
        }

        using var finalization = SandboxFinalization.Create(
            context.Request.Deadline,
            context.ExecutionRight.ExpiresAt);
        try
        {
            await context.WriteStateAsync(
                SandboxJournalState.SourceTrashed,
                finalization.Token).ConfigureAwait(false);
            context.FaultPlan.Trigger(SandboxFaultPoint.AfterSourceTrash);
            var trashFacts = await context.FactsIfPresentAsync(
                context.TrashPath,
                finalization.Token).ConfigureAwait(false);
            if (trashFacts != context.Item.ExpectedSource)
            {
                return context.Failed(OperationPortOutcome.ManualReviewRequired);
            }

            await context.WriteStateAsync(
                SandboxJournalState.Complete,
                finalization.Token).ConfigureAwait(false);
            return context.Completed(trashFacts);
        }
        catch (OperationCanceledException) when (finalization.IsCancellationRequested)
        {
            return context.Failed(OperationPortOutcome.ManualReviewRequired);
        }
    }
}
