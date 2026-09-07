using AssetLibrary.CoreServer.Hosting.Trial;
using Npgsql;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class TrialConfigurationTests
{
    [TestMethod]
    public async Task PrivateConfigurationLoadsWithoutChangingAssets()
    {
        using var fixture = new TrialConfigurationFixture();
        var configuration = await TrialConfiguration.LoadAsync(fixture.ConfigurationPath, CancellationToken.None);

        Assert.AreEqual(fixture.Configuration.DeploymentId, configuration.DeploymentId);
        Assert.AreEqual("https", configuration.Origin.Scheme);
        Assert.HasCount(1, configuration.StorageSources);
        Assert.AreEqual("read-only fixture", File.ReadAllText(fixture.AssetPath));
        Assert.DoesNotContain(configuration.StatePath, configuration.ToString(), StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow("http://localhost:7443")]
    [DataRow("https://localhost:7443/path")]
    [DataRow("https://localhost:7443/?query=value")]
    [DataRow("https://localhost:7443/#fragment")]
    [DataRow("https://user@localhost:7443")]
    [DataRow("https://localhost:443")]
    public void InsecureOrAmbiguousOriginsAreRejected(string origin)
    {
        using var fixture = new TrialConfigurationFixture();
        var exception = Assert.ThrowsExactly<TrialConfigurationException>(() =>
            TrialConfigurationValidator.Validate(fixture.Configuration with { PublicOrigin = origin }, fixture.ConfigurationPath));

        Assert.AreEqual("trial_https_origin_invalid", exception.Code);
    }

    [TestMethod]
    public void StateSecretsAndConfiguredSourcesCannotCrossBoundaries()
    {
        using var fixture = new TrialConfigurationFixture();
        var outside = fixture.Configuration with { AuthorizationKeyFile = fixture.AssetPath };
        var secretFailure = Assert.ThrowsExactly<TrialConfigurationException>(() =>
            TrialConfigurationValidator.Validate(outside, fixture.ConfigurationPath));
        var overlap = fixture.Configuration with
        {
            StorageSources = [fixture.Configuration.StorageSources[0] with { AllowedRoot = fixture.Configuration.StatePath }],
        };
        var overlapFailure = Assert.ThrowsExactly<TrialConfigurationException>(() =>
            TrialConfigurationValidator.Validate(overlap, fixture.ConfigurationPath));

        Assert.AreEqual("trial_configuration_outside_state", secretFailure.Code);
        Assert.AreEqual("trial_storage_boundary_overlap", overlapFailure.Code);
    }

    [TestMethod]
    public void SourceIdentityAndKeyAreDeploymentOwnedAndUnique()
    {
        using var fixture = new TrialConfigurationFixture();
        var source = fixture.Configuration.StorageSources[0];
        var duplicated = fixture.Configuration with { StorageSources = [source, source] };
        var invalidKey = fixture.Configuration with { StorageSources = [source with { SourceKey = "../outside" }] };

        Assert.ThrowsExactly<TrialConfigurationException>(() =>
            TrialConfigurationValidator.Validate(duplicated, fixture.ConfigurationPath));
        Assert.ThrowsExactly<TrialConfigurationException>(() =>
            TrialConfigurationValidator.Validate(invalidKey, fixture.ConfigurationPath));
    }

    [TestMethod]
    public async Task BoundAndCancellationAreEnforcedBeforeUsingConfiguration()
    {
        using var fixture = new TrialConfigurationFixture();
        await Assert.ThrowsExactlyAsync<TrialConfigurationException>(async () =>
            await TrialPrivateState.ReadTextAsync(fixture.ConfigurationPath, 8, CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await TrialPrivateState.ReadTextAsync(fixture.ConfigurationPath, 64 * 1024, cancelled.Token));
    }

    [TestMethod]
    public void RemoteDatabaseRequiresVerifiedTlsAndRuntimeSettingsRemainBounded()
    {
        const string remote = "Host=db.example.invalid;Database=trial;Username=runtime;SSL Mode=Prefer";
        Assert.ThrowsExactly<TrialConfigurationException>(() => TrialDatabaseConnections.Parse(remote));
        var safe = TrialDatabaseConnections.Parse(remote.Replace("Prefer", "VerifyFull", StringComparison.Ordinal));
        var local = TrialDatabaseConnections.Parse("Host=127.0.0.1;Database=trial;Username=runtime;Timeout=99;Command Timeout=99");

        Assert.AreEqual(SslMode.VerifyFull, safe.SslMode);
        Assert.AreEqual(5, local.Timeout);
        Assert.AreEqual(5, local.CommandTimeout);
        Assert.IsFalse(local.IncludeErrorDetail);
        Assert.IsFalse(local.NoResetOnClose);
    }

    [TestMethod]
    public async Task UnknownConfigurationFieldsAreRejected()
    {
        using var fixture = new TrialConfigurationFixture();
        var json = File.ReadAllText(fixture.ConfigurationPath);
        File.WriteAllText(fixture.ConfigurationPath, json.Insert(1, "\"production_file_writes_enabled\":true,"));

        await Assert.ThrowsExactlyAsync<TrialConfigurationException>(async () =>
            await TrialConfiguration.LoadAsync(fixture.ConfigurationPath, CancellationToken.None));
    }

    [TestMethod]
    public async Task ConfigurationReadableByOtherUsersIsRejected()
    {
        using var fixture = new TrialConfigurationFixture();
        if (OperatingSystem.IsWindows())
        {
            var file = new FileInfo(fixture.ConfigurationPath);
            var security = file.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.Read, AccessControlType.Allow));
            file.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(fixture.ConfigurationPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        var exception = await Assert.ThrowsExactlyAsync<TrialConfigurationException>(async () =>
            await TrialConfiguration.LoadAsync(fixture.ConfigurationPath, CancellationToken.None));
        Assert.AreEqual("trial_state_permissions_unsafe", exception.Code);
    }
}
