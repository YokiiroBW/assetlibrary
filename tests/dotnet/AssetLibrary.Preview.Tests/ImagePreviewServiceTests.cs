using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Application;
using AssetLibrary.Modules.PreviewProvider.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class ImagePreviewServiceTests
{
    [TestMethod]
    public async Task QueryDisposesItsDecoderOnceAndOutstandingLeaseCanStillClose()
    {
        var scenario = new ImageServiceScenario();
        var service = scenario.Service();
        var lease = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None);
        service.Dispose();
        service.Dispose();
        await lease.DisposeAsync();
        Assert.AreEqual(1, scenario.DecoderDisposals);
        Assert.AreEqual(1, scenario.Disposed);
    }

    [TestMethod]
    public async Task CapacityRemainsReservedUntilTheResponseLeaseIsDisposed()
    {
        var scenario = new ImageServiceScenario();
        using var service = scenario.Service();
        await using var first = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None);
        await using var second = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None);
        var rejection = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.Busy, rejection.Failure);
        await first.DisposeAsync();
        await using var next = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None);
        Assert.AreEqual(3, scenario.Opened);
    }

    [TestMethod]
    public async Task CacheReusesOnlyDecodeAndStillAcquiresAndVerifiesTheCurrentSource()
    {
        var scenario = new ImageServiceScenario();
        using var service = scenario.Service();
        await using (var first = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Preview, CancellationToken.None))
        {
            await first.VerifySourceAsync(CancellationToken.None);
        }
        await using (var second = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Preview, CancellationToken.None))
        {
            scenario.Changed = true;
            var failure = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () => await second.VerifySourceAsync(CancellationToken.None));
            Assert.AreEqual(ImagePreviewFailure.SourceChanged, failure.Failure);
        }
        Assert.AreEqual(2, scenario.Opened);
        Assert.AreEqual(1, scenario.Decoded);
        Assert.AreEqual(2, scenario.Disposed);
    }

    [TestMethod]
    public async Task OfflineAndNonFilesFailBeforeAnyContentRead()
    {
        var scenario = new ImageServiceScenario { Offline = true };
        using var service = scenario.Service();
        var offline = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.Unavailable, offline.Failure);
        var folder = scenario.Source with { Entry = scenario.Source.Entry with { Kind = AssetEntryKind.Directory, ContentLength = null } };
        var unsupported = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await service.PrepareAsync(folder, ImagePreviewVariant.Thumbnail, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.Unsupported, unsupported.Failure);
        Assert.AreEqual(0, scenario.Opened);
    }

    [TestMethod]
    public void LeastRecentlyUsedEntriesAreEvictedAtTheHardCountBoundary()
    {
        var cache = new ImagePreviewCache();
        for (var index = 0; index < 256; index++) cache.Store(index.ToString(System.Globalization.CultureInfo.InvariantCulture), [1]);
        Assert.IsNotNull(cache.Find("0"));
        cache.Store("next", [2]);
        Assert.IsNotNull(cache.Find("0"));
        Assert.IsNull(cache.Find("1"));
    }

    [TestMethod]
    public async Task ExpiredStartupKeepsItsAdmissionUntilItsActualCleanupCompletes()
    {
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scenario = new ImageServiceScenario { PendingCleanup = cleanup.Task };
        using var service = scenario.Service();
        var expired = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.Timeout, expired.Failure);
        Assert.AreEqual(1, scenario.Disposed);
        scenario.PendingCleanup = null;
        await using var active = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None);
        var busy = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.Busy, busy.Failure);
        cleanup.SetResult();
        await AssertAdmissionReturnsAsync(service, scenario);
    }

    [TestMethod]
    public async Task SourceReapDelayRetainsItsSlotAfterTheResponseHasFinished()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scenario = new ImageServiceScenario { SourceCleanup = completion.Task };
        using var service = scenario.Service();
        var first = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None);
        await first.DisposeAsync();
        scenario.SourceCleanup = null;
        await using var second = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None);
        var busy = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.Busy, busy.Failure);
        completion.SetResult();
        await AssertAdmissionReturnsAsync(service, scenario);
    }

    [TestMethod]
    public async Task FailedSourceCleanupQuarantinesCapacityInsteadOfAdmittingUnboundedWorkers()
    {
        var scenario = new ImageServiceScenario { SourceCleanupFails = true };
        using var service = scenario.Service();
        var first = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None);
        var second = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None);
        await first.DisposeAsync();
        await second.DisposeAsync();
        var busy = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.Busy, busy.Failure);
    }

    private static async Task AssertAdmissionReturnsAsync(ImagePreviewService service, ImageServiceScenario scenario)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (true)
        {
            try
            {
                await using var recovered = await service.PrepareAsync(scenario.Source, ImagePreviewVariant.Thumbnail, deadline.Token);
                break;
            }
            catch (ImagePreviewException failure) when (failure.Failure == ImagePreviewFailure.Busy)
            {
                await Task.Delay(10, deadline.Token);
            }
        }
    }
}

internal sealed class ImageServiceScenario : ILibraryScanTargetQuery, IImageSourceReader, IImageDecoder, IDisposable
{
    public ImagePreviewSource Source { get; } = new(LibraryId.New(), new AssetObservation(StableEntryId.New(),
        new RelativeAssetPath("image.png"), AssetEntryKind.File, 100, DateTimeOffset.UtcNow));
    public int Opened { get; private set; }
    public int Decoded { get; private set; }
    public int DecoderDisposals { get; private set; }
    public void Dispose() => DecoderDisposals++;
    public int Disposed { get; set; }
    public bool Changed { get; set; }
    public bool Offline { get; init; }
    public Task? PendingCleanup { get; set; }
    public Task? SourceCleanup { get; set; }
    public bool SourceCleanupFails { get; init; }
    public ImagePreviewService Service() => new(this, this, this, NullLogger<ImagePreviewService>.Instance);
    public ValueTask<LibraryScanTarget?> FindAsync(LibraryId libraryId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<LibraryScanTarget?>(new(libraryId, StorageSourceId.New(),
            new CanonicalLibraryRoot(Path.GetTempPath(), RootPathComparison.CaseSensitive), Offline ? StorageAvailability.Offline : StorageAvailability.Online));
    public ValueTask<IImageSourceLease> OpenAsync(LibraryScanTarget target, AssetObservation entry, CancellationToken cancellationToken)
    {
        Opened++;
        return ValueTask.FromResult<IImageSourceLease>(new ScenarioSourceLease(this));
    }
    public ValueTask<byte[]> DecodeAsync(IImageSourceLease source, ImagePreviewVariant variant, CancellationToken cancellationToken)
    {
        Decoded++;
        if (PendingCleanup is not null) return ValueTask.FromException<byte[]>(new ImageDecoderCleanupPendingException(PendingCleanup));
        return ValueTask.FromResult<byte[]>([1, 2, 3]);
    }
}

internal sealed class ScenarioSourceLease(ImageServiceScenario scenario) : IImageSourceLease
{
    public long Length => 100;
    public string ContentHash => new('a', 64);
    public ValueTask CopyToAsync(Stream output, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask VerifyAsync(CancellationToken cancellationToken) => scenario.Changed
        ? ValueTask.FromException(new ImagePreviewException(ImagePreviewFailure.SourceChanged)) : ValueTask.CompletedTask;
    public ValueTask DisposeAsync()
    {
        scenario.Disposed++;
        if (scenario.SourceCleanupFails) return ValueTask.FromException(new IOException("Synthetic source cleanup failure."));
        return scenario.SourceCleanup is null ? ValueTask.CompletedTask
            : ValueTask.FromException(new ImageDecoderCleanupPendingException(scenario.SourceCleanup));
    }
}
