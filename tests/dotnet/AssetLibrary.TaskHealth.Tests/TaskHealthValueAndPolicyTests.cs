using System.Text;
using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using AssetLibrary.Modules.TaskHealth.Domain;

namespace AssetLibrary.TaskHealth.Tests;

[TestClass]
public sealed class TaskHealthValueTests
{
    [TestMethod]
    public void IdentifiersAcceptBoundedCanonicalValues()
    {
        Assert.AreEqual("scan.initial", new TaskTypeName("scan.initial").Value);
        Assert.AreEqual("AssetIdentity", new ModuleName("AssetIdentity").Value);
        Assert.AreEqual("worker.01", new LeaseOwner("worker.01").Value);
        Assert.AreEqual("storage.offline", new HealthReasonCode("storage.offline").Value);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("UPPER")]
    [DataRow("contains space")]
    [DataRow("line\nbreak")]
    public void LowercaseIdentifiersRejectAmbiguousOrLogBreakingValues(string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new LeaseOwner(value));
        Assert.ThrowsExactly<ArgumentException>(() => new TaskTypeName(value));
    }

    [TestMethod]
    public void IdempotencyKeyIsBoundedAndNeverNormalizedSilently()
    {
        Assert.AreEqual("caller-key", new TaskIdempotencyKey("caller-key").Value);
        Assert.ThrowsExactly<ArgumentException>(() => new TaskIdempotencyKey(" caller-key"));
        Assert.ThrowsExactly<ArgumentException>(() => new TaskIdempotencyKey(new string('x', 201)));
    }

    [TestMethod]
    public void JsonPayloadRequiresAnObjectWithinTheUtf8Limit()
    {
        Assert.AreEqual("{\"asset\":1}", new JsonObjectPayload("{\"asset\":1}").Value);
        Assert.ThrowsExactly<ArgumentException>(() => new JsonObjectPayload("[]"));
        Assert.ThrowsExactly<ArgumentException>(() => new JsonObjectPayload("{broken"));

        var oversized = "{\"value\":\"" + new string('界', JsonObjectPayload.MaximumUtf8Bytes) + "\"}";
        Assert.IsGreaterThan(JsonObjectPayload.MaximumUtf8Bytes, Encoding.UTF8.GetByteCount(oversized));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new JsonObjectPayload(oversized));
    }

    [TestMethod]
    public void JsonPayloadRejectsExcessiveNesting()
    {
        var nested = string.Concat(Enumerable.Repeat("{\"x\":", 65))
            + "0"
            + string.Concat(Enumerable.Repeat("}", 65));

        Assert.ThrowsExactly<ArgumentException>(() => new JsonObjectPayload(nested));
    }

    [TestMethod]
    public void HealthScopesEnforceSystemAndResourceIdentityRules()
    {
        Assert.AreEqual(HealthScopeKind.System, HealthScope.System.Kind);
        Assert.IsNull(HealthScope.System.ResourceId);
        Assert.ThrowsExactly<ArgumentException>(() => new HealthScope(HealthScopeKind.System, Guid.NewGuid()));
        Assert.ThrowsExactly<ArgumentException>(() => new HealthScope(HealthScopeKind.Library, null));
        Assert.ThrowsExactly<ArgumentException>(() => new HealthScope(HealthScopeKind.Asset, Guid.Empty));
    }
}

[TestClass]
public sealed class TaskHealthPolicyTests
{

    [TestMethod]
    public void HealthStateRequiresExactlyTheExpectedReasonShape()
    {
        var observed = new DateTimeOffset(2026, 9, 3, 1, 0, 0, TimeSpan.FromHours(8));
        HealthStatusPolicy.Validate(new HealthStatusUpdate(
            HealthScope.System,
            new HealthComponentName("database"),
            HealthState.Normal,
            null,
            observed));

        Assert.ThrowsExactly<ArgumentException>(() => HealthStatusPolicy.Validate(new HealthStatusUpdate(
            HealthScope.System,
            new HealthComponentName("database"),
            HealthState.Normal,
            new HealthReasonCode("unexpected.reason"),
            observed)));
        Assert.ThrowsExactly<ArgumentException>(() => HealthStatusPolicy.Validate(new HealthStatusUpdate(
            HealthScope.System,
            new HealthComponentName("database"),
            HealthState.Offline,
            null,
            observed)));
    }

    [TestMethod]
    public void CancellationPolicySeparatesQueuedLeasedAndTerminalTasks()
    {
        Assert.AreEqual(TaskCancellationStatus.Cancelled, DurableTaskStatePolicy.DecideCancellation(DurableTaskState.Queued));
        Assert.AreEqual(TaskCancellationStatus.Requested, DurableTaskStatePolicy.DecideCancellation(DurableTaskState.Leased));
        Assert.AreEqual(
            TaskCancellationStatus.AlreadyCancelled,
            DurableTaskStatePolicy.DecideCancellation(DurableTaskState.Cancelled));
        Assert.AreEqual(
            TaskCancellationStatus.AlreadyTerminal,
            DurableTaskStatePolicy.DecideCancellation(DurableTaskState.Succeeded));
    }

    [TestMethod]
    public void ExpiredTaskLeasePrioritizesCancellationThenAttemptLimit()
    {
        Assert.AreEqual(
            ExpiredTaskLeaseDisposition.Cancel,
            DurableTaskStatePolicy.DecideExpiredLease(Snapshot(1, 3, cancellationRequested: true)));
        Assert.AreEqual(
            ExpiredTaskLeaseDisposition.Fail,
            DurableTaskStatePolicy.DecideExpiredLease(Snapshot(3, 3, cancellationRequested: false)));
        Assert.AreEqual(
            ExpiredTaskLeaseDisposition.Requeue,
            DurableTaskStatePolicy.DecideExpiredLease(Snapshot(2, 3, cancellationRequested: false)));
    }

    [TestMethod]
    public void RetryableTaskFinishRequiresBoundedDelayAndFailureCode()
    {
        var identity = TestData.TaskIdentity();
        var taskId = DurableTaskId.New();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DurableTaskStatePolicy.ValidateFinish(
            new DurableTaskFinishRequest(
                taskId,
                identity,
                DurableTaskFinishKind.RetryableFailure,
                new FailureCode("transient.io"))));
        Assert.ThrowsExactly<ArgumentException>(() => DurableTaskStatePolicy.ValidateFinish(
            new DurableTaskFinishRequest(
                taskId,
                identity,
                DurableTaskFinishKind.Succeeded,
                new FailureCode("unexpected.failure"))));
    }

    [TestMethod]
    public void FinishPolicyMakesSuccessWinARaceAndCancellationWinFailures()
    {
        var cancelled = Snapshot(1, 3, cancellationRequested: true);

        Assert.AreEqual(
            DurableTaskState.Succeeded,
            DurableTaskStatePolicy.DecideFinish(cancelled, DurableTaskFinishKind.Succeeded));
        Assert.AreEqual(
            DurableTaskState.Cancelled,
            DurableTaskStatePolicy.DecideFinish(cancelled, DurableTaskFinishKind.RetryableFailure));
        Assert.AreEqual(
            DurableTaskState.Queued,
            DurableTaskStatePolicy.DecideFinish(
                Snapshot(1, 3, cancellationRequested: false),
                DurableTaskFinishKind.RetryableFailure));
        Assert.AreEqual(
            DurableTaskState.Failed,
            DurableTaskStatePolicy.DecideFinish(
                Snapshot(3, 3, cancellationRequested: false),
                DurableTaskFinishKind.RetryableFailure));
        Assert.ThrowsExactly<InvalidOperationException>(() => DurableTaskStatePolicy.DecideFinish(
            Snapshot(1, 3, cancellationRequested: false),
            DurableTaskFinishKind.Cancelled));
    }

    [TestMethod]
    public void OutboxAttemptPolicyRetriesBeforeDeadLettering()
    {
        Assert.AreEqual(ExpiredOutboxLeaseDisposition.Retry, OutboxStatePolicy.DecideExpiredLease(1, 2));
        Assert.AreEqual(ExpiredOutboxLeaseDisposition.DeadLetter, OutboxStatePolicy.DecideExpiredLease(2, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OutboxStatePolicy.DecideExpiredLease(0, 2));
    }

    [TestMethod]
    public void ExecutionLimitsCannotExceedAbsoluteSafetyCaps()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TaskHealthExecutionLimits(TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TaskHealthExecutionLimits(
            TimeSpan.FromSeconds(1),
            TaskHealthExecutionLimits.AbsoluteMaximumClaimBatchSize + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TaskHealthExecutionLimits(
            TimeSpan.FromSeconds(1),
            maximumReclaimBatchSize: TaskHealthExecutionLimits.AbsoluteMaximumReclaimBatchSize + 1));
    }

    private static DurableTaskSnapshot Snapshot(int attempts, int maxAttempts, bool cancellationRequested) =>
        new(DurableTaskId.New(), DurableTaskState.Leased, attempts, maxAttempts, cancellationRequested);
}
