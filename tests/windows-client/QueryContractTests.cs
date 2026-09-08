using System.Net;
using System.Text.Json.Nodes;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class QueryContractTests
{
    [TestMethod]
    public async Task UsesOriginGeneratedEnvelopeAndBoundedServerQuery()
    {
        using var handler = new ProtocolFixture(async (request, _) =>
        {
            Assert.AreEqual("https://fixture.example:5443", request.Headers.GetValues("Origin").Single());
            var json = JsonNode.Parse(await request.Content!.ReadAsStringAsync(CancellationToken.None))!.AsObject();
            var body = json["body"]!.AsObject();
            Assert.AreEqual(100, body["page_size"]!.GetValue<int>());
            Assert.AreEqual("size", body["sort_by"]!.GetValue<string>());
            Assert.AreEqual("directories", body["kind"]!.GetValue<string>());
            return await ProtocolFixture.ResultAsync(request, ProtocolFixture.Page());
        });
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(1));
        var page = await new ReadOnlyClient(transport).EntriesAsync(new WorkspaceLocation(ProtocolFixture.Library,
            Options: new BrowseQuery("size", "desc", "directories")), null, CancellationToken.None);
        Assert.HasCount(1, page.Items);
    }

    [TestMethod]
    public async Task OversizedPageAndWrongLibraryAreRejected()
    {
        var response = ProtocolFixture.Page();
        response["items"] = new JsonArray(Enumerable.Range(0, 101).Select(index => (JsonNode)ProtocolFixture.EntryJson(index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());
        using var handler = new ProtocolFixture((request, _) => ProtocolFixture.ResultAsync(request, response));
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(1));
        var client = new ReadOnlyClient(transport);
        await Assert.ThrowsExactlyAsync<ClientException>(() => client.EntriesAsync(new WorkspaceLocation(ProtocolFixture.Library), null, CancellationToken.None));
        response["items"] = new JsonArray(ProtocolFixture.EntryJson());
        response["library"]!["library_id"] = "other-library";
        await Assert.ThrowsExactlyAsync<ClientException>(() => client.EntriesAsync(new WorkspaceLocation(ProtocolFixture.Library), null, CancellationToken.None));
    }

    [TestMethod]
    public async Task DetailScopeAndUInt64OverflowAreRejected()
    {
        var entry = ProtocolFixture.EntryJson();
        var response = new JsonObject { ["library"] = ProtocolFixture.LibraryJson, ["entry"] = entry };
        using var handler = new ProtocolFixture((request, _) => ProtocolFixture.ResultAsync(request, response));
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(1));
        var client = new ReadOnlyClient(transport);
        var requested = new EntryItem(ProtocolFixture.Library, new AssetEntry("requested-id", "library-a", "fixture.txt", "fixture.txt", "file", "42", DateTimeOffset.UtcNow));
        await Assert.ThrowsExactlyAsync<ClientException>(() => client.DetailAsync(requested, CancellationToken.None));
        entry["entry_id"] = "requested-id";
        entry["content_length"] = "18446744073709551616";
        await Assert.ThrowsExactlyAsync<ClientException>(() => client.DetailAsync(requested, CancellationToken.None));
    }

    [TestMethod]
    public async Task SearchCannotAcceptEntryOutsideFixedScope()
    {
        var hit = new JsonObject { ["library"] = ProtocolFixture.LibraryJson, ["entry"] = ProtocolFixture.EntryJson(path: "other/fixture.txt"), ["hit_reason"] = "name" };
        using var handler = new ProtocolFixture((request, _) => ProtocolFixture.ResultAsync(request, new JsonObject { ["items"] = new JsonArray(hit), ["next_cursor"] = null }));
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(1));
        await Assert.ThrowsExactlyAsync<ClientException>(() => new ReadOnlyClient(transport).EntriesAsync(new WorkspaceLocation(ProtocolFixture.Library,
            ParentPath: "selected", Query: "fixture", SearchScope: "directory"), null, CancellationToken.None));
    }

}
