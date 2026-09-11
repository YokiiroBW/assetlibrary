using System.Text.Json.Nodes;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class HostSnapshotCapacityTests
{
    [TestMethod]
    public async Task RetainedPagesAndTokensExpireAtCapacity()
    {
        using var handler = new ProtocolFixture(async (request, _) =>
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(CancellationToken.None))!.AsObject();
            return await ProtocolFixture.ResultAsync(request, body["operation"]!.GetValue<string>() == "libraries.list"
                ? HostTestSupport.Libraries(100) : ProtocolFixture.Page());
        });
        var clock = new HostTestSupport.ManualClock();
        using var transport = HostTestSupport.CreateTransport(handler);
        await using var store = HostTestSupport.CreateStore(transport, clock);
        var root = await HostTestSupport.SettledAsync(store);
        for (var index = 0; index < 63; ++index)
        { Assert.AreEqual(SnapshotStatus.Ready, (await HostTestSupport.SettledAsync(store, HostTestSupport.Open(root, root.Items[index]))).Status); }
        Assert.AreEqual(64, store.Counts.Pages);
        Assert.AreEqual(SnapshotStatus.Expired, store.Query(HostTestSupport.Open(root, root.Items[63])).Status);
        Assert.AreEqual((0, 0, 0), store.Counts);
        root = await HostTestSupport.SettledAsync(store);
        var epoch = root.Epoch;
        for (var index = 0; index < 82; ++index)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            root = await HostTestSupport.SettledAsync(store);
            Assert.IsLessThanOrEqualTo(8192, store.Counts.Tokens);
        }
        Assert.AreNotEqual(epoch, root.Epoch);
    }
}
