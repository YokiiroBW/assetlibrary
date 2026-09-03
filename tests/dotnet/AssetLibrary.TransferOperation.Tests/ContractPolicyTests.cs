using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;
using AssetLibrary.Modules.TransferSync.Contracts;
using AssetLibrary.Modules.TransferSync.Domain;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class ContractPolicyTests
{
    [TestMethod]
    [DataRow("../escape")]
    [DataRow("C:\\drive")]
    [DataRow("source/token")]
    [DataRow("source:token")]
    public void LocationTokensRejectPathSyntax(string value) =>
        Assert.ThrowsExactly<ArgumentException>(() => new TransferLocationToken(value));

    [TestMethod]
    public void Sha256RequiresCanonicalLowercaseHex()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Sha256Digest(new string('A', 64)));
        Assert.ThrowsExactly<ArgumentException>(() => new Sha256Digest(new string('0', 63)));
    }

    [TestMethod]
    public void TrashForbidsTargetAndCopyRequiresTarget()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => OperationPlanPolicy.ValidateRequest(
                OperationTestData.Plan(
                    PhysicalOperationKind.Trash,
                    [OperationTestData.Item(target: "unexpected")])));
        Assert.ThrowsExactly<ArgumentException>(
            () => OperationPlanPolicy.ValidateRequest(
                OperationTestData.Plan(
                    PhysicalOperationKind.Copy,
                    [OperationTestData.Item(target: null)])));
    }

    [TestMethod]
    public void PlanItemLimitIsEnforced()
    {
        var items = Enumerable.Range(0, OperationPlanPolicy.MaximumItems + 1)
            .Select(index => OperationTestData.Item($"source_{index}", $"target_{index}"))
            .ToArray();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OperationPlanPolicy.ValidateRequest(OperationTestData.Plan(items: items)));
    }

    [TestMethod]
    public void FailedTransferCannotClaimVerifiedTarget()
    {
        var request = TransferTestData.Request();
        var receipt = new TransferPortReceipt(
            request.SessionId,
            TransferPortOutcome.TargetExists,
            0,
            request.ExpectedSource);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => TransferStatePolicy.FromReceipt(request, receipt));
    }

    [TestMethod]
    public void TerminalStatesCannotMoveBackward()
    {
        Assert.IsFalse(
            TransferStatePolicy.CanTransition(
                TransferSessionState.Completed,
                TransferSessionState.Executing));
        Assert.IsFalse(
            OperationPlanPolicy.CanTransition(
                OperationPlanState.Completed,
                OperationPlanState.Executing));
        Assert.IsTrue(
            TransferStatePolicy.CanTransition(
                TransferSessionState.Executing,
                TransferSessionState.Completed));
        Assert.IsTrue(
            OperationPlanPolicy.CanTransition(
                OperationPlanState.Executing,
                OperationPlanState.Completed));
    }

}
