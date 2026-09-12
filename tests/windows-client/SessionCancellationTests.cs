using System.Net;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class SessionCancellationTests
{
    [TestMethod]
    public async Task DisconnectCancelsPendingLoginAndClearsRememberedCredentials()
    {
        var path = SessionTestSupport.TemporaryPath();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var persistence = new UserConnectionStore(path);
            await using var coordinator = new UserSessionCoordinator(persistence, _ => SessionTestSupport.Transport(async (_, token) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return SessionTestSupport.Login();
            }));
            var pending = coordinator.ExecuteAsync(SessionTestSupport.Connect(), CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(ControlError.Busy, (await coordinator.ExecuteAsync(SessionTestSupport.Connect(), CancellationToken.None)).ErrorCode);
            var disconnect = await coordinator.ExecuteAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Disconnect), CancellationToken.None);
            Assert.IsTrue(disconnect.Ok);
            Assert.AreEqual(ControlError.Cancelled, (await pending).ErrorCode);
            Assert.AreEqual(ConnectionState.Disconnected, coordinator.Status.State);
            Assert.IsNull(await persistence.LoadRememberedAsync(CancellationToken.None));
            Assert.HasCount(0, coordinator.Query(HostTestSupport.Root).Items);
        }
        finally { if (Directory.Exists(path)) { Directory.Delete(path, true); } }
    }

}
