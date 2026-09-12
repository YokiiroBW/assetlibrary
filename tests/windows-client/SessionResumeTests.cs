using System.Net;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class SessionResumeTests
{
    [TestMethod]
    public async Task RejectedRememberedLoginIsAttemptedOnceAndForgotten()
    {
        var path = SessionTestSupport.TemporaryPath();
        var attempts = 0;
        try
        {
            var persistence = new UserConnectionStore(path);
            await persistence.SaveAsync(SessionTestSupport.Connect().Connection! with { RememberLogin = true }, CancellationToken.None);
            await using var coordinator = new UserSessionCoordinator(persistence, _ => SessionTestSupport.Transport((_, _) =>
            {
                ++attempts;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }));
            await coordinator.InitializeAsync(CancellationToken.None);
            Assert.AreEqual(ConnectionState.AccessDenied, coordinator.Status.State);
            Assert.IsNull(await persistence.LoadRememberedAsync(CancellationToken.None));
            await Task.Delay(700);
            Assert.AreEqual(1, attempts);
            Assert.HasCount(0, coordinator.Query(HostTestSupport.Root).Items);
        }
        finally { if (Directory.Exists(path)) { Directory.Delete(path, true); } }
    }
}
