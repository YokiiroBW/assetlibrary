using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal static class SandboxOperationComposition
{
    public static OperationPlanService CreateService(
        SandboxFixture fixture,
        SandboxAccessPolicy accessPolicy,
        SandboxFaultPlan faultPlan)
    {
        var journal = CreateJournal(fixture);
        return OperationTestData.Service(
            new MemoryOperationStore(),
            new SandboxOperationPreflightPort(
                fixture,
                accessPolicy,
                new SandboxFileHash()),
            new SandboxOperationExecutorPort(
                fixture,
                accessPolicy,
                journal,
                new SandboxFileHash(),
                new SandboxStreamCopy(),
                faultPlan),
            new CaptureOperationEvents());
    }

    public static ValueTask<PayloadFacts> HashAsync(
        SandboxFixture fixture,
        TransferLocationToken location,
        CancellationToken cancellationToken) =>
        new SandboxFileHash().ComputeAsync(
            fixture.Resolve(location),
            cancellationToken);

    public static ValueTask<SandboxRecoveryResult> RecoverAsync(
        SandboxFixture fixture,
        OperationPlanId planId,
        OperationItemId itemId,
        CancellationToken cancellationToken) =>
        new SandboxRecovery(
            fixture.Boundary,
            CreateJournal(fixture),
            new SandboxFileHash()).RecoverAsync(
                planId,
                itemId,
                cancellationToken);

    private static SandboxJournalStore CreateJournal(SandboxFixture fixture) =>
        new(fixture.Boundary);
}
