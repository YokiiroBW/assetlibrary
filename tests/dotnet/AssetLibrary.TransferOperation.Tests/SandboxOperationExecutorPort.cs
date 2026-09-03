using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class SandboxOperationExecutorPort(
    SandboxFixture fixture,
    SandboxAccessPolicy accessPolicy,
    SandboxJournalStore journalStore,
    SandboxFileHash fileHash,
    SandboxStreamCopy streamCopy,
    SandboxFaultPlan faultPlan) : IOperationItemExecutorPort
{
    public async ValueTask<OperationPortReceipt> ExecuteAsync(
        OperationPlanRequest request,
        OperationItemRequest item,
        OperationItemPreflight preflight,
        OperationExecutionRight executionRight,
        CancellationToken cancellationToken)
    {
        var preflightFailure = MapDecision(preflight.Decision);
        if (preflightFailure.HasValue)
        {
            return Failure(request, item, preflightFailure.Value);
        }

        var accessFailure = MapDecision(accessPolicy.AccessDecision(item));
        if (accessFailure.HasValue)
        {
            return Failure(request, item, accessFailure.Value);
        }

        SandboxOperationContext context;
        try
        {
            context = new(
                fixture,
                journalStore,
                fileHash,
                streamCopy,
                faultPlan,
                request,
                item,
                executionRight);
        }
        catch (InvalidOperationException)
        {
            return Failure(request, item, OperationPortOutcome.UnsafeSandbox);
        }

        var observed = await context.FactsIfPresentAsync(
            context.SourcePath,
            cancellationToken).ConfigureAwait(false);
        if (observed is null)
        {
            return context.Failed(OperationPortOutcome.SourceMissing);
        }

        if (observed != item.ExpectedSource)
        {
            return context.Failed(OperationPortOutcome.SourceChanged);
        }

        if (context.TargetPath is not null
            && (File.Exists(context.TargetPath) || Directory.Exists(context.TargetPath)))
        {
            return context.Failed(OperationPortOutcome.TargetExists);
        }

        if (preflight.RequiredBytes > accessPolicy.AvailableBytes(fixture.Root))
        {
            return context.Failed(OperationPortOutcome.InsufficientSpace);
        }

        await context.WriteStateAsync(
            SandboxJournalState.Prepared,
            cancellationToken).ConfigureAwait(false);
        return request.Operation switch
        {
            PhysicalOperationKind.Copy => await SandboxCopyMoveOperation.RunAsync(
                context,
                moveSource: false,
                cancellationToken).ConfigureAwait(false),
            PhysicalOperationKind.Move => await SandboxCopyMoveOperation.RunAsync(
                context,
                moveSource: true,
                cancellationToken).ConfigureAwait(false),
            PhysicalOperationKind.Rename or PhysicalOperationKind.Restore =>
                await SandboxRenameRestoreOperation.RunAsync(
                    context,
                    cancellationToken).ConfigureAwait(false),
            PhysicalOperationKind.Trash => await SandboxTrashOperation.RunAsync(
                context,
                cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("The sandbox operation is unsupported."),
        };
    }

    private static OperationPortOutcome? MapDecision(OperationPreflightDecision decision) =>
        decision switch
        {
            OperationPreflightDecision.Ready => null,
            OperationPreflightDecision.PermissionDenied => OperationPortOutcome.PermissionDenied,
            OperationPreflightDecision.Protected => OperationPortOutcome.Protected,
            OperationPreflightDecision.SourceMissing => OperationPortOutcome.SourceMissing,
            OperationPreflightDecision.SourceChanged => OperationPortOutcome.SourceChanged,
            OperationPreflightDecision.TargetExists => OperationPortOutcome.TargetExists,
            OperationPreflightDecision.InsufficientSpace => OperationPortOutcome.InsufficientSpace,
            OperationPreflightDecision.UnsafeSandbox => OperationPortOutcome.UnsafeSandbox,
            _ => throw new InvalidOperationException("The preflight decision is unknown."),
        };

    private static OperationPortReceipt Failure(
        OperationPlanRequest request,
        OperationItemRequest item,
        OperationPortOutcome outcome) =>
        new(request.PlanId, item.ItemId, outcome, null);
}
