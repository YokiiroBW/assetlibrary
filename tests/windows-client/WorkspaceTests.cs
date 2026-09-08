using System.Net;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class WorkspaceTests
{
    [TestMethod]
    public async Task PageIsReplacedAndTransientFailureIsMarkedStale()
    {
        var count = 0;
        using var handler = new ProtocolFixture((request, _) => ++count < 3
            ? ProtocolFixture.ResultAsync(request, ProtocolFixture.Page(count == 1 ? "first.txt" : "second.txt", count == 1 ? "cursor" : null))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(1));
        using var state = new WorkspaceState(new ReadOnlyClient(transport));
        await state.NavigateAsync(new WorkspaceLocation(ProtocolFixture.Library));
        await state.NextPageAsync();
        Assert.HasCount(1, state.Entries);
        Assert.AreEqual("second.txt", state.Entries[0].Name);
        Assert.AreEqual(2, state.PageNumber);
        await state.RefreshAsync();
        Assert.IsTrue(state.IsStale);
        Assert.HasCount(1, state.Entries);
    }

    [TestMethod]
    public async Task RejectionClearsEntriesDetailsAndNavigationHistory()
    {
        var reject = false;
        using var handler = new ProtocolFixture((request, _) => reject
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))
            : ProtocolFixture.ResultAsync(request, ProtocolFixture.Page()));
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(1));
        using var state = new WorkspaceState(new ReadOnlyClient(transport));
        await state.NavigateAsync(new WorkspaceLocation());
        await state.NavigateAsync(new WorkspaceLocation(ProtocolFixture.Library));
        Assert.IsTrue(state.CanBack);
        reject = true;
        await state.RefreshAsync();
        Assert.HasCount(0, state.Entries);
        Assert.IsNull(state.Detail);
        Assert.IsFalse(state.CanBack);
        Assert.IsTrue(state.AccessRevoked);
    }

    [TestMethod]
    public async Task LateResponseCannotOverwriteNewNavigation()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var handler = new ProtocolFixture(async (request, _) =>
        {
            if (++count == 1) { firstStarted.SetResult(); await firstReleased.Task; return await ProtocolFixture.ResultAsync(request, ProtocolFixture.Page("old.txt")); }
            return await ProtocolFixture.ResultAsync(request, ProtocolFixture.Page("new.txt"));
        });
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(3));
        using var state = new WorkspaceState(new ReadOnlyClient(transport));
        var old = state.NavigateAsync(new WorkspaceLocation(ProtocolFixture.Library));
        await firstStarted.Task;
        await state.NavigateAsync(new WorkspaceLocation(ProtocolFixture.Library, Options: new BrowseQuery("size")));
        firstReleased.SetResult();
        await old;
        Assert.AreEqual("new.txt", state.Entries.Single().Name);
    }

    [TestMethod]
    public async Task CancelledPageCannotAppendItsCursorToNewLocation()
    {
        var pageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var handler = new ProtocolFixture(async (request, _) =>
        {
            var call = ++count;
            if (call == 2) { pageStarted.SetResult(); await pageReleased.Task; }
            return await ProtocolFixture.ResultAsync(request, ProtocolFixture.Page(call == 3 ? "new.txt" : "old.txt", call == 1 ? "old-cursor" : null));
        });
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(3));
        using var state = new WorkspaceState(new ReadOnlyClient(transport));
        await state.NavigateAsync(new WorkspaceLocation(ProtocolFixture.Library));
        var oldPage = state.NextPageAsync();
        await pageStarted.Task;
        await state.NavigateAsync(new WorkspaceLocation(ProtocolFixture.Library, Options: new BrowseQuery("modified")));
        pageReleased.SetResult();
        await oldPage;
        Assert.AreEqual(1, state.PageNumber);
        Assert.AreEqual("new.txt", state.Entries.Single().Name);
    }
}
