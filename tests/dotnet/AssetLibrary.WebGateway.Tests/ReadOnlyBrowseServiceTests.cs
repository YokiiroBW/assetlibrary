using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class BrowseResultValidationTests
{
    [TestMethod]
    public async Task RejectsRowsOutsideRequestedDirectory()
    {
        var libraryId = LibraryId.New();
        var fake = new FakeAuthorizedReadModelQuery
        {
            Browse = (query, _) => ValueTask.FromResult<AuthorizedEntryPage?>(
                new AuthorizedEntryPage(
                    GatewayTestData.Library(libraryId),
                    query.ParentPath,
                    new ReadPage<ReadOnlyEntry>(
                        [GatewayTestData.Entry(libraryId, "other/file.txt")],
                        null))),
        };
        var request = new BrowseEntriesQuery(
            new AuthenticatedSubject("subject"),
            libraryId,
            new BrowseParentPath("folder"),
            ReadPageOptions.Default);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await GatewayTestData.Service(fake).BrowseEntriesAsync(
                request,
                CancellationToken.None));
    }
}

[TestClass]
public sealed class PageResultValidationTests
{
    [TestMethod]
    public async Task RejectsAnUnboundedPortResult()
    {
        var items = Enumerable.Range(0, 2)
            .Select(_ => GatewayTestData.Library(LibraryId.New()))
            .ToArray();
        var fake = new FakeAuthorizedReadModelQuery();
        fake.List = (_, _) => ValueTask.FromResult(new ReadPage<AuthorizedLibrary>(items, null));
        var request = new ListLibrariesQuery(
            new AuthenticatedSubject("subject"),
            new ReadPageOptions(1, TimeSpan.FromSeconds(1)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await GatewayTestData.Service(fake).ListLibrariesAsync(
                request,
                CancellationToken.None));
    }
}

[TestClass]
public sealed class ReadDeadlineTests
{
    [TestMethod]
    public async Task DeadlineBecomesTimeoutButCallerCancellationPropagates()
    {
        var fake = new FakeAuthorizedReadModelQuery
        {
            List = static async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return new ReadPage<AuthorizedLibrary>([], null);
            },
        };
        var request = new ListLibrariesQuery(
            new AuthenticatedSubject("subject"),
            new ReadPageOptions(1, TimeSpan.FromMilliseconds(100)));

        await Assert.ThrowsExactlyAsync<TimeoutException>(
            async () => await GatewayTestData.Service(fake).ListLibrariesAsync(
                request,
                CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => await GatewayTestData.Service(fake).ListLibrariesAsync(
                request,
                cancellation.Token));
    }
}
