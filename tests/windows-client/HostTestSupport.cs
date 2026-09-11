using System.Text.Json.Nodes;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

internal static class HostTestSupport
{
    internal static SnapshotRequest Root => new(7, Guid.Empty, Guid.Empty);
    internal static SnapshotRequest Open(SnapshotResponse page, SnapshotItem item) => new(8, page.Epoch, item.Node);
    internal static ClientTransport CreateTransport(ProtocolFixture handler) =>
        new(ProtocolFixture.Profile, handler, TimeSpan.FromSeconds(3));
    internal static SnapshotStore CreateStore(ClientTransport transport, TimeProvider? clock = null) =>
        new(new ReadOnlyClient(transport), (clock ?? TimeProvider.System).GetUtcNow().AddHours(1), clock);

    internal static string RequiredNativeProfilePath()
    {
        var path = Environment.GetEnvironmentVariable("ASSETLIBRARY_NATIVE_TEST_PROFILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        { Assert.Inconclusive("A running isolated Core fixture and private connection profile are required."); }
        return path!;
    }
    internal static JsonObject Libraries(int count = 1) => new()
    {
        ["items"] = new JsonArray(Enumerable.Range(0, count).Select(index =>
        {
            var library = ProtocolFixture.LibraryJson;
            library["display_name"] = "库 " + index;
            return (JsonNode)library;
        }).ToArray()),
        ["next_cursor"] = null,
    };

    internal static async Task<SnapshotResponse> SettledAsync(SnapshotStore store, SnapshotRequest? request = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var response = store.Query(request ?? Root);
            if (response.Status is not (SnapshotStatus.Loading or SnapshotStatus.Busy)) { return response; }
            await Task.Delay(5, deadline.Token);
        }
    }

    internal sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
        internal void Advance(TimeSpan amount) => Interlocked.Add(ref ticks, amount.Ticks);
    }
}
