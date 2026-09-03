using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class DefaultValueObjectTests
{
    [TestMethod]
    public void NestedDefaultDigestIsRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new PayloadFacts(0, default));
    }

    [TestMethod]
    public void OperationPlanRejectsDefaultIdentifiersAndTokens()
    {
        var valid = OperationTestData.Plan();
        Assert.ThrowsExactly<ArgumentException>(
            () => OperationPlanPolicy.ValidateRequest(valid with { PlanId = default }));
        Assert.ThrowsExactly<ArgumentException>(
            () => OperationPlanPolicy.ValidateRequest(
                valid with
                {
                    Items = [valid.Items[0] with { Source = default }],
                }));
    }

    [TestMethod]
    public void ExecutionRightsRejectNestedDefaultValues()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new TransferExecutionRight(
                default,
                TransferLeaseToken.New(),
                1,
                DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.ThrowsExactly<ArgumentException>(
            () => new OperationExecutionRight(
                default,
                OperationLeaseToken.New(),
                1,
                DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [TestMethod]
    public async Task TransferServiceRejectsDefaultLocationBeforeCallingPort()
    {
        var port = new StubTransferPort();
        var service = TransferTestData.Service(new MemoryTransferStore(), port);
        var request = TransferTestData.Request() with { Source = default };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await service.ExecuteAsync(
                request,
                TransferTestData.Right(),
                CancellationToken.None));
        Assert.AreEqual(0, port.Calls);
    }
}
