using System.IO.Pipes;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ThumbnailJobTests
{
    [TestMethod]
    public async Task CancellingOwnedJobTerminatesChildBlockedOnItsAnonymousInput()
    {
        using var job = new ThumbnailDecodeJob();
        using var input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        using var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var handles = new[] { input.ClientSafePipeHandle.DangerousGetHandle(), output.ClientSafePipeHandle.DangerousGetHandle(), error.ClientSafePipeHandle.DangerousGetHandle() };
        using var startup = new ThumbnailDecodeStartup(job.Handle.DangerousGetHandle(), handles);
        using var process = startup.Start(ThumbnailTestSupport.Executable, handles);
        input.DisposeLocalCopyOfClientHandle(); output.DisposeLocalCopyOfClientHandle(); error.DisposeLocalCopyOfClientHandle();
        using var before = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<OperationCanceledException>(() => ThumbnailDecoder.WaitForExitAsync(process, before.Token));
        job.Terminate();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await ThumbnailDecoder.WaitForExitAsync(process, cleanup.Token);
        Assert.AreEqual(0, await output.ReadAsync(new byte[1], cleanup.Token));
    }
}
