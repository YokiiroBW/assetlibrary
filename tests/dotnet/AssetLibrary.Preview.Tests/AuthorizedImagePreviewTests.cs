using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Contracts;
using AssetLibrary.WebGateway.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class AuthorizedImagePreviewTests
{
    [TestMethod]
    public async Task InvisibleEntryCannotReachTheImageSourceOrCache()
    {
        var scenario = new AuthorizedImageScenario();
        var failure = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await scenario.Service.GetAsync(scenario.Query, ImagePreviewVariant.Thumbnail, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.NotFound, failure.Failure);
        Assert.AreEqual(0, scenario.Images.Prepared);
    }

    [TestMethod]
    public async Task RevokedPermissionDisposesPreparedBytesBeforeTheyCanBeReturned()
    {
        var scenario = new AuthorizedImageScenario();
        scenario.Reads.GetEntry = (_, _) => ValueTask.FromResult(scenario.Reads.CallCount == 1 ? scenario.Detail : null);
        var failure = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await scenario.Service.GetAsync(scenario.Query, ImagePreviewVariant.Preview, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.NotFound, failure.Failure);
        Assert.AreEqual(1, scenario.Images.Prepared);
        Assert.IsTrue(scenario.Images.Lease.Disposed);
        Assert.AreEqual(0, scenario.Images.Lease.Verified);
    }

    [TestMethod]
    public async Task ReplacedIndexedObservationCannotReturnThePreparedVersion()
    {
        var scenario = new AuthorizedImageScenario();
        scenario.Reads.GetEntry = (_, _) => ValueTask.FromResult<AuthorizedEntryDetail?>(scenario.Reads.CallCount == 1
            ? scenario.Detail : scenario.Detail with { Entry = scenario.Detail.Entry with { LastWriteTimeUtc = scenario.Detail.Entry.LastWriteTimeUtc.AddSeconds(1) } });
        var failure = await Assert.ThrowsExactlyAsync<ImagePreviewException>(async () =>
            await scenario.Service.GetAsync(scenario.Query, ImagePreviewVariant.Preview, CancellationToken.None));
        Assert.AreEqual(ImagePreviewFailure.SourceChanged, failure.Failure);
        Assert.IsTrue(scenario.Images.Lease.Disposed);
    }
}

internal sealed class AuthorizedImageScenario
{
    public FakeAuthorizedReadModelQuery Reads { get; } = new();
    public AuthorizedImageQuery Images { get; } = new();
    public AuthorizedEntryDetail Detail { get; }
    public GetEntryQuery Query { get; }
    public AuthorizedImagePreviewService Service { get; }
    public AuthorizedImageScenario()
    {
        var library = LibraryId.New();
        var entry = StableEntryId.New();
        Detail = new AuthorizedEntryDetail(new AuthorizedLibrary(library, "Synthetic", StorageAvailability.Online, LibraryAccessLevel.ReadOnly),
            new ReadOnlyEntry(entry, library, new RelativeAssetPath("image.png"), AssetEntryKind.File, 12, DateTimeOffset.UtcNow));
        Query = new GetEntryQuery(new AuthenticatedSubject(Guid.NewGuid().ToString("D")), library, entry, ReadPageOptions.DefaultTimeout);
        Service = new AuthorizedImagePreviewService(new ReadOnlyBrowseService(Reads, NullLogger<ReadOnlyBrowseService>.Instance), Images);
    }
}

internal sealed class AuthorizedImageQuery : IImagePreviewQuery
{
    public int Prepared { get; private set; }
    public AuthorizedImageLease Lease { get; } = new();
    public ValueTask<IImagePreviewLease> PrepareAsync(ImagePreviewSource source, ImagePreviewVariant variant, CancellationToken cancellationToken)
    {
        Prepared++;
        return ValueTask.FromResult<IImagePreviewLease>(Lease);
    }
}

internal sealed class AuthorizedImageLease : IImagePreviewLease
{
    public bool Disposed { get; private set; }
    public int Verified { get; private set; }
    public ReadOnlyMemory<byte> Png => new byte[] { 1, 2, 3 };
    public ValueTask VerifySourceAsync(CancellationToken cancellationToken) { Verified++; return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
