using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

/// <summary>
/// Test-only trusted scope provider, deliberately the only <see cref="IMediaPackageInspectionScopeQuery"/>
/// implementation shipped by this task. There is no production authorization adapter here; the real
/// one arrives with the persistent publication work.
/// </summary>
internal sealed class MediaPackageScopeStub : IMediaPackageInspectionScopeQuery
{
    private MediaPackageInspectionScope? scope;

    public MediaPackageInspectionScope? Scope => scope;

    public int ResolveCalls { get; private set; }

    public int CurrentCalls { get; private set; }

    /// <summary>
    /// When false the provider denies, which must keep the preflight away from every directory and
    /// file.
    /// </summary>
    public bool Authorized { get; set; } = true;

    /// <summary>
    /// Revokes the scope once this many <see cref="IsCurrentAsync"/> calls have completed, modelling a
    /// permission that lapses while the preflight is running.
    /// </summary>
    public int? RevokeAfterCurrentCalls { get; set; }

    public void Grant(
        MediaPackageSandbox sandbox,
        string revision = "scope-rev-1",
        DateTimeOffset? expiresAt = null,
        StorageAvailability availability = StorageAvailability.Online)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        scope = new MediaPackageInspectionScope(
            revision,
            expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
            availability,
            sandbox.StagingToken,
            sandbox.LibraryToken);
    }

    public void GrantRaw(MediaPackageInspectionScope grantedScope) => scope = grantedScope;

    public ValueTask<MediaPackageInspectionScope?> ResolveAsync(
        MediaPackageCallerContext caller,
        string stagingRef,
        LibraryId libraryId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        ResolveCalls++;
        return ValueTask.FromResult(Authorized ? scope : null);
    }

    public ValueTask<bool> IsCurrentAsync(
        MediaPackageInspectionScope currentScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentScope);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Authorized || scope is null || scope != currentScope)
        {
            return ValueTask.FromResult(false);
        }

        var revoked = RevokeAfterCurrentCalls is { } limit && CurrentCalls >= limit;
        CurrentCalls++;
        return ValueTask.FromResult(!revoked && scope.ExpiresAt > DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Test-only volume space observation. The real adapter reads the local drive; the narrow injection
/// point exists so the insufficient-space and unknown-space paths are provable without a global
/// storage abstraction.
/// </summary>
internal sealed class MediaPackageSpaceStub : IMediaPackageVolumeSpaceObserver, IDisposable
{
    private readonly ManualResetEventSlim entered = new(false);

    public long? AvailableBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    public int Calls { get; private set; }

    /// <summary>
    /// When set, the observation waits on this task so a test can hold one preflight inside the run.
    /// </summary>
    public TaskCompletionSource? Block { get; set; }

    public ManualResetEventSlim Entered => entered;

    public void Dispose() => entered.Dispose();

    public async ValueTask<long?> ObserveAvailableBytesAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        entered.Set();
        if (Block is { } block)
        {
            await block.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return AvailableBytes;
    }
}

/// <summary>
/// Deterministic clock so report timestamps and the elapsed-budget verdict are provable.
/// </summary>
internal sealed class MediaPackageClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now => now;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now = now.Add(delta);
}

/// <summary>
/// Clock whose budget token is already cancelled, so the elapsed-budget path is provable without
/// waiting or sleeping.
/// </summary>
internal sealed class MediaPackageExpiredClock : TimeProvider
{
    private bool fired;

    public override DateTimeOffset GetUtcNow() => new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!fired)
        {
            fired = true;
            callback(state);
        }

        return new CancelledTimer();
    }

    private sealed class CancelledTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
