using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationRecovery
{
    public static async Task AssertRecoveryAsync(TrialHostIntegrationFixture host, TrialHostIntegrationSession oldSession)
    {
        const string replacement = "V01-020 rotated administrator passphrase";
        var request = new AdministratorOperatorRequest(Guid.NewGuid(), Guid.NewGuid(),
            AdministratorBootstrapRecoveryAction.RecoverAdministrator, new LocalAccountName("trial-admin"), null,
            DateTimeOffset.UtcNow.AddMinutes(5));
        using var secret = new LocalSecret(replacement);
        host.Authentication.Risk.Available = false;
        var unavailable = await host.Runtime.RecoverAdministratorAsync(request, secret, CancellationToken.None);
        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.DependencyUnavailable, unavailable.Outcome);
        Assert.AreEqual(200, (await TrialHostIntegrationHttp.SendAsync(host, oldSession, HttpMethod.Get,
            "/assetlink/v1/auth/session")).Status);
        host.Authentication.Risk.Available = true;
        await host.Runtime.RotateAuthorizationKeyAsync(CancellationToken.None);
        var recovered = await host.Runtime.RecoverAdministratorAsync(request, secret, CancellationToken.None);
        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.Applied, recovered.Outcome);
        var replay = await host.Runtime.RecoverAdministratorAsync(request, secret, CancellationToken.None);
        Assert.IsTrue(replay.WasReplayed);
        Assert.AreEqual(recovered.Account!.CredentialVersion, replay.Account!.CredentialVersion);
        var revoked = await TrialHostIntegrationHttp.SendAsync(host, oldSession, HttpMethod.Get, "/assetlink/v1/auth/session");
        Assert.AreEqual(401, revoked.Status);
        Assert.IsTrue(revoked.SetsCookie);
        var current = await TrialHostIntegrationHttp.SignInAsync(host, password: replacement);
        Assert.AreEqual(oldSession.PrincipalId, current.PrincipalId);
    }
}
