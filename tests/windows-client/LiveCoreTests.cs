using System.Text.Json.Nodes;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class LiveCoreTests
{
    [TestMethod]
    [TestCategory("NativeLive")]
    public async Task RealHttpsCoreAuthenticatesPagesSearchesDetailsAndRevokes()
    {
        var path = Environment.GetEnvironmentVariable("ASSETLIBRARY_NATIVE_TEST_PROFILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Assert.Inconclusive("A running isolated Core fixture and private connection profile are required."); }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(path, deadline.Token))!.AsObject();
        string Value(string name) => settings[name]!.GetValue<string>();
        var profile = new ServerProfile(Value("origin"), Value("certificate_sha256"));
        using var transport = new ClientTransport(profile);
        var session = await transport.SignInAsync(Value("account_name"), Value("password"), deadline.Token);
        var confirmed = await transport.SessionAsync(deadline.Token);
        Assert.AreEqual(session.PrincipalId, confirmed.PrincipalId);
        var client = new ReadOnlyClient(transport);
        var libraries = await client.LibrariesAsync(null, null, deadline.Token);
        var library = libraries.Items.Single(item => item.Id == Value("library_id"));
        var first = await client.EntriesAsync(new WorkspaceLocation(library), null, deadline.Token);
        Assert.HasCount(100, first.Items);
        Assert.IsNotNull(first.NextCursor);
        var second = await client.EntriesAsync(new WorkspaceLocation(library), first.NextCursor, deadline.Token);
        Assert.IsNotEmpty(second.Items);
        Assert.IsFalse(first.Items.Select(item => item.Entry.Id).Intersect(second.Items.Select(item => item.Entry.Id), StringComparer.Ordinal).Any());
        var selected = first.Items.First(item => item.Entry.Kind == "file");
        var detail = await client.DetailAsync(selected, deadline.Token);
        Assert.AreEqual(selected.Entry.Id, detail.Entry.Id);
        var search = await client.EntriesAsync(new WorkspaceLocation(library, Query: selected.Name, SearchScope: "library"), null, deadline.Token);
        Assert.IsTrue(search.Items.Any(item => item.Entry.Id == selected.Entry.Id));
        await transport.SignOutAsync(deadline.Token);
        var revoked = await Assert.ThrowsExactlyAsync<ClientException>(() => transport.SessionAsync(deadline.Token));
        Assert.AreEqual(401, revoked.Status);
        await VerifyInvisibleAsync(profile, settings, library, deadline.Token);
    }

    private static async Task VerifyInvisibleAsync(ServerProfile profile, JsonObject settings, LibraryItem library, CancellationToken token)
    {
        using var transport = new ClientTransport(profile);
        _ = await transport.SignInAsync(settings["invisible_account_name"]!.GetValue<string>(), settings["invisible_account_password"]!.GetValue<string>(), token);
        var client = new ReadOnlyClient(transport);
        var libraries = await client.LibrariesAsync(null, null, token);
        Assert.HasCount(0, libraries.Items);
        var refusal = await Assert.ThrowsExactlyAsync<ClientException>(() => client.EntriesAsync(new WorkspaceLocation(library), null, token));
        Assert.AreEqual(404, refusal.Status);
        await transport.SignOutAsync(token);
    }
}
