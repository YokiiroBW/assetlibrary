using System.Net;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class SessionExpiryTests
{
    [TestMethod]
    public async Task ExpiryClearsSessionWithoutAnotherUserQuery()
    {
        var path = SessionTestSupport.TemporaryPath();
        try
        {
            var persistence = new UserConnectionStore(path);
            await using var coordinator = new UserSessionCoordinator(persistence, _ => SessionTestSupport.Transport(async (request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/auth/login", StringComparison.Ordinal)) { return SessionTestSupport.Login(DateTimeOffset.UtcNow.AddMilliseconds(600)); }
                if (request.RequestUri.AbsolutePath.EndsWith("/auth/logout", StringComparison.Ordinal)) { return new(HttpStatusCode.NoContent); }
                return await ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries());
            }));
            Assert.IsTrue((await coordinator.ExecuteAsync(SessionTestSupport.Connect() with { Connection = SessionTestSupport.Connect().Connection! with { RememberLogin = true } }, CancellationToken.None)).Ok);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (coordinator.Status.State == ConnectionState.Connected) { await Task.Delay(20, deadline.Token); }
            Assert.AreEqual(ConnectionState.AccessDenied, coordinator.Status.State);
            Assert.IsNull(coordinator.Status.DisplayName);
            Assert.HasCount(0, coordinator.Query(HostTestSupport.Root).Items);
            Assert.IsNull(await persistence.LoadRememberedAsync(CancellationToken.None));
        }
        finally { if (Directory.Exists(path)) { Directory.Delete(path, true); } }
    }

}
