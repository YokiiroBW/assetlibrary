using System.Text.Json.Nodes;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class HostLiveTests
{
    [TestMethod]
    [TestCategory("NativeLive")]
    public async Task RealHttpsHostProjectsNestedPagesAndSeparatesInaccessibleIdentity()
    {
        var path = HostTestSupport.RequiredNativeProfilePath();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(path, deadline.Token))!.AsObject();
        var profile = await PrivateProfile.LoadAsync(path, deadline.Token);
        using var transport = new ClientTransport(profile.Server);
        var session = await transport.SignInAsync(profile.Account, profile.Password, deadline.Token);
        SnapshotRequest oldLibrary;
        try
        {
            await using var store = new SnapshotStore(new ReadOnlyClient(transport), session.ExpiresAt);
            var root = await HostTestSupport.SettledAsync(store);
            Assert.AreEqual(SnapshotStatus.Ready, root.Status);
            oldLibrary = HostTestSupport.Open(root, root.Items.Single(item => item.Kind == SnapshotKind.Library));
            var first = await HostTestSupport.SettledAsync(store, oldLibrary);
            Assert.AreEqual(SnapshotStatus.Ready, first.Status);
            Assert.HasCount(101, first.Items);
            var nextItem = first.Items.Single(item => item.Kind == SnapshotKind.NextPage);
            var second = await HostTestSupport.SettledAsync(store, HostTestSupport.Open(first, nextItem));
            Assert.AreEqual(SnapshotStatus.Ready, second.Status);
            Assert.IsNotEmpty(second.Items);
            Assert.IsFalse(first.Items.Where(item => item.Kind != SnapshotKind.NextPage).Select(item => item.Name)
                .Intersect(second.Items.Select(item => item.Name), StringComparer.Ordinal).Any());
            var directory = first.Items.Concat(second.Items).Single(item => item.Kind == SnapshotKind.Directory && item.Name == "相册");
            var nested = await HostTestSupport.SettledAsync(store, HostTestSupport.Open(first, directory));
            Assert.AreEqual(SnapshotStatus.Ready, nested.Status);
            var summer = nested.Items.Single(item => item.Kind == SnapshotKind.Directory && item.Name == "夏日");
            var leaf = await HostTestSupport.SettledAsync(store, HostTestSupport.Open(nested, summer));
            Assert.AreEqual(SnapshotStatus.Ready, leaf.Status);
            Assert.IsTrue(leaf.Items.Any(item => item.Kind == SnapshotKind.File && item.Name == "preview-placeholder.jpg"));
        }
        finally { await transport.SignOutAsync(deadline.Token); }
        using var invisible = new ClientTransport(profile.Server);
        var invisibleSession = await invisible.SignInAsync(settings["invisible_account_name"]!.GetValue<string>(),
            settings["invisible_account_password"]!.GetValue<string>(), deadline.Token);
        try
        {
            await using var store = new SnapshotStore(new ReadOnlyClient(invisible), invisibleSession.ExpiresAt);
            var root = await HostTestSupport.SettledAsync(store);
            Assert.AreEqual(SnapshotStatus.Ready, root.Status);
            Assert.HasCount(0, root.Items);
            Assert.AreEqual(SnapshotStatus.Expired, store.Query(oldLibrary).Status);
        }
        finally { await invisible.SignOutAsync(deadline.Token); }
    }
}
