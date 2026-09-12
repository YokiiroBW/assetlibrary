using System.Net;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class SessionCoordinatorTests
{
    [TestMethod]
    public async Task SwitchIdentityClearsOldEpochAndRejectsFailedLoginWithoutRetry()
    {
        var path = SessionTestSupport.TemporaryPath();
        var attempts = 0;
        var notifications = 0;
        try
        {
            await using var coordinator = new UserSessionCoordinator(new UserConnectionStore(path), _ => SessionTestSupport.Transport(async (request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/auth/login", StringComparison.Ordinal))
                {
                    ++attempts;
                    return attempts == 1 ? SessionTestSupport.Login() : new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
                if (request.RequestUri.AbsolutePath.EndsWith("/auth/logout", StringComparison.Ordinal)) { return new(HttpStatusCode.NoContent); }
                return await ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries());
            }), () => ++notifications);
            Assert.AreEqual(SnapshotStatus.Unavailable, coordinator.Query(HostTestSupport.Root).Status);
            var first = await coordinator.ExecuteAsync(SessionTestSupport.Connect(), CancellationToken.None);
            Assert.IsTrue(first.Ok);
            var old = await SessionTestSupport.ReadyAsync(coordinator);
            var rejected = await coordinator.ExecuteAsync(SessionTestSupport.Connect("other"), CancellationToken.None);
            Assert.AreEqual(ControlError.AccessDenied, rejected.ErrorCode);
            var current = coordinator.Query(HostTestSupport.Root);
            Assert.AreEqual(SnapshotStatus.AccessDenied, current.Status);
            Assert.AreNotEqual(old.Epoch, current.Epoch);
            Assert.HasCount(0, current.Items);
            Assert.IsNull(coordinator.Status.DisplayName);
            await Task.Delay(700);
            Assert.AreEqual(2, attempts);
            Assert.IsGreaterThanOrEqualTo(3, notifications);
        }
        finally { if (Directory.Exists(path)) { Directory.Delete(path, true); } }
    }

}
