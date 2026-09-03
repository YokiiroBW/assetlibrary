using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;
using AssetLibrary.Modules.TransferSync.Contracts;
using AssetLibrary.Modules.TransferSync.Domain;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class TransferStoredResultValidationTests
{
    [TestMethod]
    public void FailureMustMatchItsState()
    {
        var request = TransferTestData.Request();
        var invalid = new TransferResult(
            request.SessionId,
            TransferSessionState.Cancelled,
            TransferFailureKind.TargetExists,
            0,
            null);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => TransferStatePolicy.ValidateStoredResult(request, invalid));
    }
}

[TestClass]
public sealed class OperationStoredResultValidationTests
{
    [TestMethod]
    public void UnknownItemFailureIsRejected()
    {
        var request = OperationTestData.Plan();
        var prepared = new PreparedOperationPlan(
            request,
            OperationPlanState.AwaitingConfirmation,
            new OperationPreflightSnapshot(
                request.PlanId,
                request.Items.Select(item => new OperationItemPreflight(
                    item.ItemId,
                    OperationPreflightDecision.Ready,
                    item.ExpectedSource,
                    item.ExpectedSource.Length,
                    item.ExpectedSource.Length)).ToArray(),
                new OperationConfirmationDigest(new string('0', 64)),
                request.Deadline));
        var result = new OperationPlanResult(
            request.PlanId,
            OperationPlanState.Failed,
            [new OperationItemResult(
                request.Items[0].ItemId,
                false,
                (OperationItemFailure)999,
                null)]);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => OperationPlanPolicy.ValidateResult(prepared, result));
    }
}
