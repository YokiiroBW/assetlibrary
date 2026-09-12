using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class SessionNotificationTests
{
    [TestMethod]
    public void NotificationInitializesStaEvenWhenCallerIsMta()
    {
        var exitCode = -1;
        var caller = new Thread(() => exitCode = ShellSessionNotifier.Notify(probeOnly: true));
        caller.SetApartmentState(ApartmentState.MTA);
        caller.Start();
        Assert.IsTrue(caller.Join(TimeSpan.FromSeconds(4)));
        Assert.AreEqual(0, exitCode);
    }
}
