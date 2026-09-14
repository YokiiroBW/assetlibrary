using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class ServiceLibraryReadGrantTests
{
    [TestMethod]
    public async Task InvalidTargetsNeverReachTheStore()
    {
        var store = new RecordingStore();
        var service = new ServiceLibraryReadGrantService(store, TimeProvider.System);
        LibraryId library = LibraryId.New();
        foreach (var ids in new LibraryId[][] { [], [default], [library, library], Enumerable.Range(0, 101).Select(_ => LibraryId.New()).ToArray() })
        {
            await Assert.ThrowsExactlyAsync<ReadOnlyTrialException>(async () =>
                await service.SetAsync(new(Guid.NewGuid(), ids, true), Operation(), CancellationToken.None));
        }
        await Assert.ThrowsExactlyAsync<ReadOnlyTrialException>(async () =>
            await service.SetAsync(new(Guid.Empty, [library], true), Operation(), CancellationToken.None));
        Assert.AreEqual(0, store.Calls);
    }

    [TestMethod]
    public async Task InvalidAuditIdentityAndCancellationNeverReachTheStore()
    {
        var store = new RecordingStore();
        var service = new ServiceLibraryReadGrantService(store, TimeProvider.System);
        var request = new ServiceLibraryReadGrantChange(Guid.NewGuid(), [LibraryId.New()], false);
        foreach (var operation in new ServiceLibraryReadGrantOperation[]
        {
            new("", Guid.NewGuid()), new(" operator ", Guid.NewGuid()), new("operator\n", Guid.NewGuid()),
            new(new string('x', 201), Guid.NewGuid()), new("operator", Guid.Empty),
        })
        {
            await Assert.ThrowsExactlyAsync<ReadOnlyTrialException>(async () =>
                await service.SetAsync(request, operation, CancellationToken.None));
        }
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await service.SetAsync(request, Operation(), cancellation.Token));
        Assert.AreEqual(0, store.Calls);
    }

    [TestMethod]
    public async Task GrantAndRevokeFreezeTheAuthorizedLibraryBatch()
    {
        var store = new RecordingStore();
        var service = new ServiceLibraryReadGrantService(store, TimeProvider.System);
        var principal = Guid.NewGuid();
        var original = LibraryId.New();
        List<LibraryId> libraries = [original];
        var operation = Operation();
        foreach (var granted in new[] { true, false })
        {
            libraries[0] = original;
            await service.SetAsync(new(principal, libraries, granted), operation, CancellationToken.None);
            libraries[0] = LibraryId.New();
            Assert.AreEqual(original, store.Request!.LibraryIds[0]);
            Assert.AreEqual(principal, store.Request.PrincipalId);
            Assert.AreEqual(granted, store.Request.Granted);
            Assert.AreEqual(operation, store.Operation);
        }
        Assert.AreEqual(2, store.Calls);
    }

    private static ServiceLibraryReadGrantOperation Operation() => new("synthetic-local-operator", Guid.NewGuid());

    private sealed class RecordingStore : IServiceLibraryReadGrantStore
    {
        public int Calls { get; private set; }
        public ServiceLibraryReadGrantChange? Request { get; private set; }
        public ServiceLibraryReadGrantOperation? Operation { get; private set; }

        public ValueTask SetAsync(ServiceLibraryReadGrantChange request, ServiceLibraryReadGrantOperation operation,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Operation = operation;
            return ValueTask.CompletedTask;
        }
    }
}
