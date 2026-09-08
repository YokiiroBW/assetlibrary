using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.Modules.PreviewProvider.Infrastructure;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class ImageDecoderCleanupTests
{
    [TestMethod]
    public async Task FailedLateStartupWaitsForItsOwnedChildCleanupBeforeCompleting()
    {
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = Task.FromException<IImageChildProcess>(
            new ImageChildCleanupPendingException(cleanup.Task, new IOException("Synthetic startup failure.")));
        var reaper = IsolatedImageDecoder.ReapLateStartupAsync(startup);
        Assert.IsFalse(reaper.IsCompleted);
        cleanup.SetResult();
        await reaper.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task FailedLateStartupPreservesTheActualCleanupFailure()
    {
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = Task.FromException<IImageChildProcess>(
            new ImageChildCleanupPendingException(cleanup.Task, new IOException("Synthetic startup failure.")));
        var reaper = IsolatedImageDecoder.ReapLateStartupAsync(startup);
        Assert.IsFalse(reaper.IsCompleted);
        var failure = new IOException("Synthetic cleanup failure.");
        cleanup.SetException(failure);
        var observed = await Assert.ThrowsExactlyAsync<IOException>(() => reaper.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreSame(failure, observed);
    }
}
