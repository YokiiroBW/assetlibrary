using System.Security.Claims;
using AssetLibrary.CoreServer.Adapters.AssetLink;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class LibrariesListProtocolTests
{
    [TestMethod]
    public async Task UsesAuthenticatedSubjectAndSerializesBoundedPage()
    {
        ListLibrariesQuery? captured = null;
        var libraryId = LibraryId.New();
        var fake = new FakeAuthorizedReadModelQuery
        {
            List = (query, _) =>
            {
                captured = query;
                return ValueTask.FromResult(
                    new ReadPage<AuthorizedLibrary>(
                        [GatewayTestData.Library(libraryId)],
                        new ReadPageCursor("next")));
            },
        };

        var response = await GatewayTestData.Protocol(fake).HandleAsync(
            GatewayTestData.Principal("oidc:user-1"),
            GatewayTestData.Request("libraries.list", "{\"page_size\":25}", 900),
            CancellationToken.None);

        Assert.AreEqual(200, response.StatusCode);
        Assert.IsNotNull(captured);
        Assert.AreEqual("oidc:user-1", captured.Subject.Value);
        Assert.AreEqual(25, captured.Page.PageSize);
        Assert.AreEqual(TimeSpan.FromMilliseconds(900), captured.Page.Timeout);
        var body = GatewayTestData.Body(response);
        Assert.AreEqual("next", body["next_cursor"]!.GetValue<string>());
        Assert.AreEqual(libraryId.Value.ToString("D"), body["items"]![0]!["library_id"]!.GetValue<string>());
        Assert.IsNull(body["items"]![0]!["canonical_root"]);
    }
}

[TestClass]
public sealed class EntriesBrowseProtocolTests
{
    [TestMethod]
    public async Task HidesUnavailableLibraryAndSerializesRelativeFactsOnly()
    {
        var libraryId = LibraryId.New();
        var fake = new FakeAuthorizedReadModelQuery
        {
            Browse = (query, _) => ValueTask.FromResult<AuthorizedEntryPage?>(
                query.ParentPath.Value == "missing"
                    ? null
                    : new AuthorizedEntryPage(
                        GatewayTestData.Library(libraryId),
                        query.ParentPath,
                        new ReadPage<ReadOnlyEntry>(
                            [GatewayTestData.Entry(libraryId, "folder/image.jpg")],
                            null))),
        };
        var protocol = GatewayTestData.Protocol(fake);

        var response = await protocol.HandleAsync(
            GatewayTestData.Principal("oidc:user-1"),
            GatewayTestData.Request(
                "entries.browse",
                $"{{\"library_id\":\"{libraryId.Value:D}\",\"parent_relative_path\":\"folder\"}}"),
            CancellationToken.None);

        Assert.AreEqual(200, response.StatusCode);
        var body = GatewayTestData.Body(response);
        Assert.AreEqual("folder/image.jpg", body["items"]![0]!["relative_path"]!.GetValue<string>());
        Assert.AreEqual("image.jpg", body["items"]![0]!["name"]!.GetValue<string>());
        Assert.IsNull(body["items"]![0]!["content_hash"]);

        var unavailable = await protocol.HandleAsync(
            GatewayTestData.Principal("oidc:user-1"),
            GatewayTestData.Request(
                "entries.browse",
                $"{{\"library_id\":\"{libraryId.Value:D}\",\"parent_relative_path\":\"missing\"}}"),
            CancellationToken.None);
        Assert.AreEqual(404, unavailable.StatusCode);
        Assert.AreEqual("not_found", GatewayTestData.ErrorCode(unavailable));
    }
}

[TestClass]
public sealed class AssetSearchProtocolTests
{
    [TestMethod]
    public async Task NormalizesQueryAndExplainsNameHit()
    {
        var libraryId = LibraryId.New();
        SearchAssetsQuery? captured = null;
        var fake = new FakeAuthorizedReadModelQuery
        {
            Search = (query, _) =>
            {
                captured = query;
                return ValueTask.FromResult(
                    new ReadPage<AuthorizedSearchHit>(
                        [new AuthorizedSearchHit(
                            GatewayTestData.Library(libraryId),
                            GatewayTestData.Entry(libraryId, "folder/image.jpg"),
                            SearchHitReason.Name)],
                        null));
            },
        };

        var response = await GatewayTestData.Protocol(fake).HandleAsync(
            GatewayTestData.Principal("oidc:user-1"),
            GatewayTestData.Request("assets.search", "{\"query\":\"  image   jpg  \"}"),
            CancellationToken.None);

        Assert.AreEqual(200, response.StatusCode);
        Assert.IsNotNull(captured);
        Assert.AreEqual("image jpg", captured.SearchText.Value);
        Assert.AreEqual(
            "name",
            GatewayTestData.Body(response)["items"]![0]!["hit_reason"]!.GetValue<string>());
    }
}

[TestClass]
public sealed class AssetLinkFailureProtocolTests
{
    [TestMethod]
    public async Task AnonymousMalformedAndWriteRequestsFailClosedBeforeQuery()
    {
        var fake = new FakeAuthorizedReadModelQuery();
        var protocol = GatewayTestData.Protocol(fake);
        var validList = GatewayTestData.Request("libraries.list", "{}");

        var anonymous = await protocol.HandleAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            validList,
            CancellationToken.None);
        Assert.AreEqual(401, anonymous.StatusCode);
        Assert.AreEqual("authentication_required", GatewayTestData.ErrorCode(anonymous));

        var malformed = await protocol.HandleAsync(
            GatewayTestData.Principal("oidc:user-1"),
            "{",
            CancellationToken.None);
        Assert.AreEqual(400, malformed.StatusCode);
        Assert.AreEqual("invalid_request", GatewayTestData.ErrorCode(malformed));

        foreach (var invalidBody in new[]
        {
            GatewayTestData.Request("libraries.list", "{\"page_size\":\"many\"}"),
            GatewayTestData.Request("entries.browse", "{\"library_id\":42}"),
            GatewayTestData.Request("assets.search", "{\"query\":false}"),
        })
        {
            var invalidField = await protocol.HandleAsync(
                GatewayTestData.Principal("oidc:user-1"),
                invalidBody,
                CancellationToken.None);
            Assert.AreEqual(400, invalidField.StatusCode);
            Assert.AreEqual("invalid_request", GatewayTestData.ErrorCode(invalidField));
        }

        var write = await protocol.HandleAsync(
            GatewayTestData.Principal("oidc:user-1"),
            GatewayTestData.Request("entries.delete", "{}"),
            CancellationToken.None);
        Assert.AreEqual(400, write.StatusCode);
        Assert.AreEqual("unsupported_operation", GatewayTestData.ErrorCode(write));
        Assert.AreEqual(
            "unsupported",
            AssetLinkReadRequestParser.Parse(
                GatewayTestData.Principal("oidc:user-1"),
                GatewayTestData.Request("private-data-in-operation", "{}")).Operation);

        var idempotentRead = await protocol.HandleAsync(
            GatewayTestData.Principal("oidc:user-1"),
            """
            {"message_type":"control.request","request_id":"request-1","operation":"libraries.list","body":{},"idempotency_key":"write-shape"}
            """,
            CancellationToken.None);
        Assert.AreEqual(400, idempotentRead.StatusCode);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task PortFailureAndDeadlineUseStableExternalErrors()
    {
        var unavailableFake = new FakeAuthorizedReadModelQuery
        {
            List = static (_, _) => throw new InvalidOperationException("sensitive detail"),
        };
        var unavailable = await GatewayTestData.Protocol(unavailableFake).HandleAsync(
            GatewayTestData.Principal("oidc:user-1"),
            GatewayTestData.Request("libraries.list", "{}"),
            CancellationToken.None);
        Assert.AreEqual(503, unavailable.StatusCode);
        Assert.AreEqual("service_unavailable", GatewayTestData.ErrorCode(unavailable));
        Assert.DoesNotContain("sensitive detail", unavailable.Json, StringComparison.Ordinal);

        var slowFake = new FakeAuthorizedReadModelQuery
        {
            List = static async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return new ReadPage<AuthorizedLibrary>([], null);
            },
        };
        var timeout = await GatewayTestData.Protocol(slowFake).HandleAsync(
            GatewayTestData.Principal("oidc:user-1"),
            GatewayTestData.Request("libraries.list", "{}", 100),
            CancellationToken.None);
        Assert.AreEqual(504, timeout.StatusCode);
        Assert.AreEqual("timeout", GatewayTestData.ErrorCode(timeout));
    }
}
