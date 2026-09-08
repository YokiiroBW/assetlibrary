using System.Text.Json.Nodes;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class SessionBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SessionTokenAllowsNormalHeaderAndRejectsControlCharacters(bool invalidToken)
    {
        var csrf = invalidToken ? new string('x', 41) + "\r\n" : new string('x', 43);
        using var handler = new ProtocolFixture(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/login", StringComparison.Ordinal))
            {
                return ProtocolFixture.Json(new JsonObject
                {
                    ["authenticated"] = true,
                    ["principal_id"] = "synthetic-principal",
                    ["display_name"] = "合成账号",
                    ["csrf_token"] = csrf,
                    ["absolute_expires_at"] = "2026-09-09T00:00:00Z",
                });
            }
            Assert.AreEqual(csrf, request.Headers.GetValues("X-AssetLibrary-CSRF").Single());
            return await ProtocolFixture.ResultAsync(request, new JsonObject { ["items"] = new JsonArray(), ["next_cursor"] = null });
        });
        using var transport = new ClientTransport(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(1));
        if (invalidToken)
        {
            var error = await Assert.ThrowsExactlyAsync<ClientException>(() => transport.SignInAsync("fixture", "synthetic-password", CancellationToken.None));
            Assert.AreEqual("invalid_response", error.Code);
            return;
        }
        var session = await transport.SignInAsync("fixture", "synthetic-password", CancellationToken.None);
        Assert.AreEqual(nameof(ClientSession), session.ToString());
        var libraries = await new ReadOnlyClient(transport).LibrariesAsync(null, null, CancellationToken.None);
        Assert.HasCount(0, libraries.Items);
    }
}
