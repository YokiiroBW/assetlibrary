using System.Text.Json.Nodes;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class HostSnapshotNavigationTests
{
    [TestMethod]
    public async Task RootPhysicalDirectoryAndNextPageUseOpaqueLocations()
    {
        var scopes = new List<string>();
        using var handler = new ProtocolFixture(async (request, _) =>
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(CancellationToken.None))!.AsObject();
            if (body["operation"]!.GetValue<string>() == "libraries.list") { return await ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries()); }
            var query = body["body"]!.AsObject();
            var parent = query["parent_relative_path"]!.GetValue<string>();
            scopes.Add(parent + ":" + query["cursor"]?.GetValue<string>());
            var entry = ProtocolFixture.EntryJson(path: parent.Length == 0 ? "nested" : "nested/child.txt");
            if (parent.Length == 0) { entry["kind"] = "directory"; }
            var page = new JsonObject
            {
                ["library"] = ProtocolFixture.LibraryJson,
                ["parent_relative_path"] = parent,
                ["items"] = new JsonArray(entry),
                ["next_cursor"] = query["cursor"] is null ? "opaque-cursor" : null
            };
            return await ProtocolFixture.ResultAsync(request, page);
        });
        using var transport = HostTestSupport.CreateTransport(handler);
        await using var store = HostTestSupport.CreateStore(transport);
        var root = await HostTestSupport.SettledAsync(store);
        var library = await HostTestSupport.SettledAsync(store, HostTestSupport.Open(root, root.Items.Single()));
        Assert.AreEqual(SnapshotKind.Directory, library.Items[0].Kind);
        Assert.AreEqual(SnapshotKind.NextPage, library.Items[1].Kind);
        var nested = await HostTestSupport.SettledAsync(store, HostTestSupport.Open(library, library.Items[0]));
        Assert.AreEqual(SnapshotKind.File, nested.Items[0].Kind);
        Assert.AreEqual(SnapshotStatus.Expired, store.Query(HostTestSupport.Open(nested, nested.Items[0])).Status);
        var next = await HostTestSupport.SettledAsync(store, HostTestSupport.Open(library, library.Items[1]));
        Assert.HasCount(1, next.Items);
        Assert.AreEqual(":|nested:|:opaque-cursor", string.Join("|", scopes));
        var encoded = System.Text.Encoding.Unicode.GetString(SnapshotProtocol.EncodeResponse(7, root));
        Assert.DoesNotContain("library-a", encoded);
        Assert.DoesNotContain("https:", encoded);
    }

    [TestMethod]
    public async Task InvalidDisplayNameAndReparseNeverBecomeNavigable()
    {
        var malformed = false;
        using var handler = new ProtocolFixture(async (request, _) =>
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(CancellationToken.None))!.AsObject();
            if (body["operation"]!.GetValue<string>() == "libraries.list") { return await ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries()); }
            var entry = ProtocolFixture.EntryJson(path: malformed ? "bad\0name" : "linked");
            entry["kind"] = "reparse_directory";
            var page = ProtocolFixture.Page(); page["items"] = new JsonArray(entry);
            return await ProtocolFixture.ResultAsync(request, page);
        });
        var clock = new HostTestSupport.ManualClock();
        using var transport = HostTestSupport.CreateTransport(handler);
        await using var store = HostTestSupport.CreateStore(transport, clock);
        var root = await HostTestSupport.SettledAsync(store);
        var libraryRequest = HostTestSupport.Open(root, root.Items.Single());
        var library = await HostTestSupport.SettledAsync(store, libraryRequest);
        Assert.AreEqual(SnapshotKind.Reparse, library.Items.Single().Kind);
        Assert.AreEqual(SnapshotStatus.Expired, store.Query(HostTestSupport.Open(library, library.Items.Single())).Status);
        malformed = true;
        clock.Advance(TimeSpan.FromSeconds(5));
        var invalid = await HostTestSupport.SettledAsync(store, libraryRequest);
        Assert.AreEqual(SnapshotStatus.InvalidResponse, invalid.Status);
        Assert.HasCount(0, invalid.Items);
    }
}
