using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class SandboxOperationContext
{
    private readonly SandboxJournalStore journalStore;

    public SandboxOperationContext(
        SandboxFixture fixture,
        SandboxJournalStore journalStore,
        SandboxFileHash fileHash,
        SandboxStreamCopy streamCopy,
        SandboxFaultPlan faultPlan,
        OperationPlanRequest request,
        OperationItemRequest item,
        OperationExecutionRight executionRight)
    {
        Fixture = fixture;
        this.journalStore = journalStore;
        FileHash = fileHash;
        StreamCopy = streamCopy;
        FaultPlan = faultPlan;
        Request = request;
        Item = item;
        ExecutionRight = executionRight;
        SourcePath = fixture.Resolve(item.Source);
        TargetPath = item.Target.HasValue ? fixture.Resolve(item.Target.Value) : null;
        StagePath = request.Operation is PhysicalOperationKind.Copy or PhysicalOperationKind.Move
            ? fixture.Boundary.ResolveInternal(
                "stage",
                request.PlanId.Value.ToString("N"),
                item.ItemId.Value.ToString("N"),
                "payload.partial")
            : null;
        TrashPath = fixture.InternalTrashPath(request.PlanId, item.ItemId);
        Journal = new SandboxJournalRecord(
            request.PlanId.Value,
            item.ItemId.Value,
            request.Operation,
            fixture.Boundary.ToRelative(SourcePath),
            TargetPath is null ? null : fixture.Boundary.ToRelative(TargetPath),
            StagePath is null ? null : fixture.Boundary.ToRelative(StagePath),
            fixture.Boundary.ToRelative(TrashPath),
            item.ExpectedSource.Length,
            item.ExpectedSource.Sha256.Value,
            SandboxJournalState.Prepared);
    }

    public SandboxFixture Fixture { get; }

    public SandboxFileHash FileHash { get; }

    public SandboxStreamCopy StreamCopy { get; }

    public SandboxFaultPlan FaultPlan { get; }

    public OperationPlanRequest Request { get; }

    public OperationItemRequest Item { get; }

    public OperationExecutionRight ExecutionRight { get; }

    public string SourcePath { get; }

    public string? TargetPath { get; }

    public string? StagePath { get; }

    public string TrashPath { get; }

    public SandboxJournalRecord Journal { get; private set; }

    public ValueTask WriteStateAsync(
        SandboxJournalState state,
        CancellationToken cancellationToken)
    {
        Journal = Journal with { State = state };
        return journalStore.WriteAsync(Journal, cancellationToken);
    }

    public async ValueTask<PayloadFacts?> FactsIfPresentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        Fixture.Boundary.Revalidate(path);
        return File.Exists(path)
            ? await FileHash.ComputeAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public OperationPortReceipt Completed(PayloadFacts facts) =>
        new(
            Request.PlanId,
            Item.ItemId,
            OperationPortOutcome.Completed,
            facts);

    public OperationPortReceipt Failed(OperationPortOutcome outcome) =>
        new(Request.PlanId, Item.ItemId, outcome, null);
}
