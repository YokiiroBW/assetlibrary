using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class SandboxConcurrencyTests
{
    [TestMethod]
    public async Task ConcurrentCopiesToSameTargetHaveExactlyOneWinner()
    {
        using var scenario = new SandboxScenario();
        var firstSource = scenario.Write(
            "first_source",
            "incoming/first.bin",
            PayloadTestData.Payload);
        var secondSource = scenario.Write(
            "second_source",
            "incoming/second.bin",
            PayloadTestData.Payload);
        var target = scenario.Empty("shared_target", "library/shared.bin");
        var firstRequest = SandboxScenario.Request(
            PhysicalOperationKind.Copy,
            firstSource,
            target,
            PayloadTestData.Facts());
        var secondRequest = SandboxScenario.Request(
            PhysicalOperationKind.Copy,
            secondSource,
            target,
            PayloadTestData.Facts());
        var secondService = scenario.CreateParallelService();
        var firstPrepared = await scenario.Service.PrepareAsync(
            firstRequest,
            CancellationToken.None);
        var secondPrepared = await secondService.PrepareAsync(
            secondRequest,
            CancellationToken.None);

        var results = await Task.WhenAll(
            scenario.Service.ExecuteAsync(
                firstRequest.PlanId,
                firstPrepared.Preflight.Confirmation,
                OperationTestData.Right(),
                CancellationToken.None).AsTask(),
            secondService.ExecuteAsync(
                secondRequest.PlanId,
                secondPrepared.Preflight.Confirmation,
                OperationTestData.Right(),
                CancellationToken.None).AsTask());

        Assert.AreEqual(1, results.Count(result => result.State == OperationPlanState.Completed));
        Assert.AreEqual(
            1,
            results.Count(result =>
                result.Items[0].Failure == OperationItemFailure.TargetExists));
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(firstSource)));
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(secondSource)));
        Assert.AreEqual(
            PayloadTestData.Facts(),
            await scenario.HashAsync(target));
    }
}
