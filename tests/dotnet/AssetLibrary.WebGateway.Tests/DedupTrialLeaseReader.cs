using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.Infrastructure.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// Reads what the durable task store itself recorded about one attempt's lease: its owner, when it was
/// last renewed and until when it is valid. TaskHealth keeps those columns and nothing on the wire carries
/// them, so this is the only place a test can state that a running attempt's lease was really renewed
/// rather than inferred from how long the attempt happened to take.
/// </summary>
/// <remarks>
/// It reads the store; it never writes to it. The read runs in the module's own session, so it sees exactly
/// the rows and columns the product's runtime role may see — no wider. That session is read-only here: the
/// only statement issued is a SELECT.
/// </remarks>
internal sealed class DedupTrialLeaseReader
{
    private readonly ModulePostgresSession session;

    public DedupTrialLeaseReader(TrialHostIntegrationFixture host) =>
        session = new ModulePostgresSession(
            host.Services.GetRequiredService<TrialDedupServices>().Tasks,
            ModuleDatabaseRole.TaskHealth);

    /// <summary>What one read of the lease row found. Every field is the store's own value.</summary>
    internal sealed record DedupTrialLeaseSample(
        string State,
        string? Owner,
        long Generation,
        DateTimeOffset? HeartbeatAt,
        DateTimeOffset? LeaseUntil,
        DateTimeOffset? CancellationRequestedAt);

    /// <summary>
    /// Samples the lease of one task until the worker has really claimed it, and returns what it read. A
    /// claimed attempt is the only state in which a lease can be renewed, so waiting for it is what makes
    /// the renewal assertion below about a running attempt rather than about a queued one.
    /// </summary>
    public async Task<DedupTrialLeaseSample> WaitForClaimAsync(Guid taskId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!deadline.IsCancellationRequested)
        {
            var sample = await ReadAsync(taskId);
            if (sample is { State: "leased", Generation: > 0 })
            {
                return sample;
            }

            Assert.AreNotEqual("failed", sample?.State, $"The attempt failed before it was ever claimed: {sample?.State}.");
            await Task.Delay(20, deadline.Token);
        }

        Assert.Fail($"The durable task {taskId:D} was never claimed by the worker.");
        return null!;
    }

    /// <summary>Reads the lease row once, or null when the store has no such task.</summary>
    public async Task<DedupTrialLeaseSample?> ReadAsync(Guid taskId) =>
        await session.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = ModulePostgresSession.Command(
                connection,
                transaction,
                """
                SELECT state::text, lease_owner, lease_generation, heartbeat_at, lease_until, cancellation_requested_at
                FROM task_health.durable_task
                WHERE task_id = $1
                """,
                taskId);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return (DedupTrialLeaseSample?)null;
            }

            return new DedupTrialLeaseSample(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
        }, CancellationToken.None).ConfigureAwait(false);

    /// <summary>
    /// Watches one attempt while it runs and states what the store recorded, without ever writing to it.
    /// It keeps sampling until the attempt settles or the window closes, so a test can assert that the
    /// lease advanced between two samples that were both taken while the attempt was still leased: a
    /// sample taken after it finished moves only because it finished, and would prove nothing.
    /// </summary>
    public async Task<IReadOnlyList<DedupTrialLeaseSample>> ObserveWhileActiveAsync(Guid taskId, TimeSpan window)
    {
        var taken = new List<DedupTrialLeaseSample>();
        using var deadline = new CancellationTokenSource(window);
        while (!deadline.IsCancellationRequested)
        {
            var sample = await ReadAsync(taskId);
            Assert.IsNotNull(sample, $"The durable task {taskId:D} disappeared while it was being observed.");
            taken.Add(sample);
            if (sample.State != "leased")
            {
                break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return taken;
    }
}
