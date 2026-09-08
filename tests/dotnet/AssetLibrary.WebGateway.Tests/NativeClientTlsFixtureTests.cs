using AssetLibrary.IntegrationTestSupport;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class NativeClientTlsFixtureTests
{
    [TestMethod]
    public void ExistingTlsFixturesKeepTheirOneHourDefault()
    {
        using var certificate = TrialTestTls.CreateSelfSigned();
        var remaining = certificate.NotAfter.ToUniversalTime() - DateTime.UtcNow;
        Assert.IsTrue(remaining > TimeSpan.FromMinutes(59) && remaining <= TimeSpan.FromHours(1));
        Assert.IsTrue(certificate.MatchesHostname("localhost", allowCommonName: false));
    }

    [TestMethod]
    public void NativeTlsFixtureSupportsTwoHoursWithoutChangingItsHostname()
    {
        using var certificate = TrialTestTls.CreateSelfSigned(validity: TimeSpan.FromMinutes(125));
        Assert.IsTrue(certificate.NotAfter.ToUniversalTime() - DateTime.UtcNow > TimeSpan.FromMinutes(124));
        Assert.IsTrue(certificate.MatchesHostname("localhost", allowCommonName: false));
        Assert.IsFalse(certificate.MatchesHostname("attacker.invalid", allowCommonName: false));
    }

    [TestMethod]
    public void TlsFixtureRejectsUnboundedAndExpiredLifetimes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrialTestTls.CreateSelfSigned(validity: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrialTestTls.CreateSelfSigned(validity: TimeSpan.FromDays(1)));
    }
}
