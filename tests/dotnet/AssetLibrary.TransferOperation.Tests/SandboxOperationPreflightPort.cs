using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class SandboxOperationPreflightPort(
    SandboxFixture fixture,
    SandboxAccessPolicy accessPolicy,
    SandboxFileHash fileHash) : IOperationPreflightPort
{
    public async ValueTask<IReadOnlyList<OperationItemPreflight>> InspectAsync(
        OperationPlanRequest request,
        CancellationToken cancellationToken)
    {
        var results = new List<OperationItemPreflight>(request.Items.Count);
        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(
                await InspectItemAsync(request.Operation, item, cancellationToken)
                    .ConfigureAwait(false));
        }

        return results;
    }

    private async ValueTask<OperationItemPreflight> InspectItemAsync(
        PhysicalOperationKind operation,
        OperationItemRequest item,
        CancellationToken cancellationToken)
    {
        var access = accessPolicy.AccessDecision(item);
        if (access != OperationPreflightDecision.Ready)
        {
            return Result(item, access, null, 0, 0);
        }

        try
        {
            var source = fixture.Resolve(item.Source);
            if (!File.Exists(source))
            {
                return Result(item, OperationPreflightDecision.SourceMissing, null, 0, 0);
            }

            var observed = await fileHash.ComputeAsync(source, cancellationToken).ConfigureAwait(false);
            if (observed != item.ExpectedSource)
            {
                return Result(
                    item,
                    OperationPreflightDecision.SourceChanged,
                    observed,
                    0,
                    0);
            }

            var required = RequiresCopyCapacity(operation) ? observed.Length : 0;
            var available = accessPolicy.AvailableBytes(fixture.Root);
            if (item.Target.HasValue)
            {
                var target = fixture.Resolve(item.Target.Value);
                if (File.Exists(target) || Directory.Exists(target))
                {
                    return Result(
                        item,
                        OperationPreflightDecision.TargetExists,
                        observed,
                        required,
                        available);
                }
            }

            var decision = available < required
                ? OperationPreflightDecision.InsufficientSpace
                : OperationPreflightDecision.Ready;
            return Result(item, decision, observed, required, available);
        }
        catch (InvalidOperationException)
        {
            return Result(item, OperationPreflightDecision.UnsafeSandbox, null, 0, 0);
        }
    }

    private static bool RequiresCopyCapacity(PhysicalOperationKind operation) =>
        operation is PhysicalOperationKind.Copy
            or PhysicalOperationKind.Move
            or PhysicalOperationKind.Restore;

    private static OperationItemPreflight Result(
        OperationItemRequest item,
        OperationPreflightDecision decision,
        AssetLibrary.Modules.TransferSync.Contracts.PayloadFacts? observed,
        long required,
        long available) =>
        new(item.ItemId, decision, observed, required, available);
}
