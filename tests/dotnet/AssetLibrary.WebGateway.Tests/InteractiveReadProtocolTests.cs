using System.Text.Json.Nodes;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class InteractiveReadProtocolTests
{
    [TestMethod]
    public async Task DetailsAndAnchoredBrowseCarryAuthorizedFactsAndCompleteQueryOptions()
    {
        var library = GatewayTestData.Library(LibraryId.New()) with { Category = LibraryCategory.Images, Availability = StorageAvailability.Offline };
        var entry = GatewayTestData.Entry(library.LibraryId, "folder/image.jpg");
        BrowseEntriesQuery? captured = null;
        var fake = new FakeAuthorizedReadModelQuery
        {
            GetLibrary = (_, _) => ValueTask.FromResult<AuthorizedLibrary?>(library),
            GetEntry = (_, _) => ValueTask.FromResult<AuthorizedEntryDetail?>(new(library, entry)),
            Browse = (request, _) =>
            {
                captured = request;
                return ValueTask.FromResult<AuthorizedEntryPage?>(new(library, request.ParentPath,
                    new ReadPage<ReadOnlyEntry>([entry], null), request.AnchorEntryId));
            },
        };
        var protocol = GatewayTestData.Protocol(fake);
        var body = new JsonObject { ["library_id"] = library.LibraryId.Value.ToString("D") };
        var foundLibrary = await protocol.HandleAsync(GatewayTestData.Principal("reader"),
            GatewayTestData.Request("libraries.get", body.ToJsonString()), CancellationToken.None);
        Assert.AreEqual(200, foundLibrary.StatusCode);
        Assert.AreEqual("images", GatewayTestData.Body(foundLibrary)["library"]!["category"]!.GetValue<string>());
        body["entry_id"] = entry.EntryId.Value.ToString("D");
        var foundEntry = await protocol.HandleAsync(GatewayTestData.Principal("reader"),
            GatewayTestData.Request("entries.get", body.ToJsonString()), CancellationToken.None);
        Assert.AreEqual(200, foundEntry.StatusCode);
        Assert.AreEqual("offline", GatewayTestData.Body(foundEntry)["library"]!["availability"]!.GetValue<string>());
        Assert.IsNull(GatewayTestData.Body(foundEntry)["entry"]!["content_url"]);
        body.Remove("entry_id");
        body["parent_relative_path"] = "folder";
        body["anchor_entry_id"] = entry.EntryId.Value.ToString("D");
        body["sort_by"] = "size";
        body["sort_direction"] = "desc";
        body["kind"] = "files";
        body["name_filter"] = "  image  ";
        var page = await protocol.HandleAsync(GatewayTestData.Principal("reader"),
            GatewayTestData.Request("entries.browse", body.ToJsonString()), CancellationToken.None);
        Assert.AreEqual(200, page.StatusCode);
        Assert.IsNotNull(captured);
        Assert.AreEqual(new EntryBrowseOptions(EntrySortBy.Size, ReadSortDirection.Desc, EntryKindFilter.Files, "image"), captured.Options);
        Assert.AreEqual(entry.EntryId, captured.AnchorEntryId);
        body["cursor"] = "earlier";
        var invalid = await protocol.HandleAsync(GatewayTestData.Principal("reader"),
            GatewayTestData.Request("entries.browse", body.ToJsonString()), CancellationToken.None);
        Assert.AreEqual(400, invalid.StatusCode);
        Assert.AreEqual(3, fake.CallCount);
    }

}

[TestClass]
public sealed class InteractiveReadProtocolFailureTests
{
    [TestMethod]
    public async Task InvalidOptionsAndContradictoryScopesFailBeforeTheReadPort()
    {
        var fake = new FakeAuthorizedReadModelQuery();
        var protocol = GatewayTestData.Protocol(fake);
        var library = LibraryId.New().Value.ToString("D");
        foreach (var (operation, body) in new (string, string)[]
        {
            ("libraries.list", "{\"category\":\"Images\"}"),
            ("entries.browse", "{\"library_id\":\"" + library + "\",\"sort_by\":\"0\"}"),
            ("assets.search", "{\"query\":\"image\",\"scope\":\"library\"}"),
            ("assets.search", "{\"query\":\"image\",\"library_id\":\"" + library + "\"}"),
            ("assets.search", "{\"query\":\"image\",\"scope\":\"directory\",\"library_id\":\"" + library + "\"}"),
            ("libraries.get", "{\"library_id\":\"" + library + "\",\"cursor\":\"page\"}"),
        })
        {
            var result = await protocol.HandleAsync(GatewayTestData.Principal("reader"),
                GatewayTestData.Request(operation, body), CancellationToken.None);
            Assert.AreEqual(400, result.StatusCode);
        }

        Assert.AreEqual(0, fake.CallCount);
        var hidden = await protocol.HandleAsync(GatewayTestData.Principal("reader"),
            GatewayTestData.Request("libraries.get", "{\"library_id\":\"" + library + "\"}"), CancellationToken.None);
        Assert.AreEqual(404, hidden.StatusCode);
        Assert.AreEqual("not_found", GatewayTestData.ErrorCode(hidden));
        fake.Search = static (_, _) => throw new AuthorizedReadNotFoundException();
        var search = await protocol.HandleAsync(GatewayTestData.Principal("reader"),
            GatewayTestData.Request("assets.search", "{\"query\":\"image\",\"scope\":\"library\",\"library_id\":\"" + library + "\"}"),
            CancellationToken.None);
        Assert.AreEqual(404, search.StatusCode);
    }
}
