using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class SandboxScenario : IDisposable
{
    public SandboxScenario()
    {
        Fixture = new();
        AccessPolicy = new();
        Faults = new();
        Service = SandboxOperationComposition.CreateService(
            Fixture,
            AccessPolicy,
            Faults);
    }

    public SandboxFixture Fixture { get; }

    public SandboxAccessPolicy AccessPolicy { get; }

    public SandboxFaultPlan Faults { get; }

    public OperationPlanService Service { get; }

    public TransferLocationToken Write(
        string token,
        string relativePath,
        byte[] content)
    {
        var location = Fixture.Register(token, relativePath);
        var path = Fixture.Resolve(location);
        SandboxFilePrimitives.EnsureParent(Fixture.Boundary, path);
        File.WriteAllBytes(path, content);
        return location;
    }

    public TransferLocationToken Empty(string token, string relativePath) =>
        Fixture.Register(token, relativePath);

    public SandboxTargetOperationCase TargetCase(
        PhysicalOperationKind operation,
        string sourceToken,
        string sourcePath,
        string targetToken,
        string targetPath)
    {
        var source = Write(sourceToken, sourcePath, PayloadTestData.Payload);
        var target = Empty(targetToken, targetPath);
        return new(
            source,
            target,
            Request(operation, source, target, PayloadTestData.Facts()));
    }

    public static OperationPlanRequest Request(
        PhysicalOperationKind operation,
        TransferLocationToken source,
        TransferLocationToken? target,
        PayloadFacts expected,
        string? idempotencyKey = null)
    {
        var item = new OperationItemRequest(
            OperationItemId.New(),
            source,
            target,
            expected);
        return new(
            OperationPlanId.New(),
            new OperationIdempotencyKey(
                idempotencyKey ?? $"operation-{Guid.NewGuid():N}"),
            operation,
            [item],
            DateTimeOffset.UtcNow.AddMinutes(2));
    }

    public async ValueTask<OperationPlanResult> RunAsync(
        OperationPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await Service.PrepareAsync(request, cancellationToken);
        return await Service.ExecuteAsync(
            request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            cancellationToken);
    }

    public ValueTask<PayloadFacts> HashAsync(
        TransferLocationToken location,
        CancellationToken cancellationToken = default) =>
        SandboxOperationComposition.HashAsync(Fixture, location, cancellationToken);

    public ValueTask<SandboxRecoveryResult> RecoverAsync(
        OperationPlanId planId,
        OperationItemId itemId,
        CancellationToken cancellationToken = default) =>
        SandboxOperationComposition.RecoverAsync(
            Fixture,
            planId,
            itemId,
            cancellationToken);

    public OperationPlanService CreateParallelService() =>
        SandboxOperationComposition.CreateService(
            Fixture,
            AccessPolicy,
            new SandboxFaultPlan());

    public void Dispose() => Fixture.Dispose();
}

internal sealed record SandboxTargetOperationCase(
    TransferLocationToken Source,
    TransferLocationToken Target,
    OperationPlanRequest Request);
