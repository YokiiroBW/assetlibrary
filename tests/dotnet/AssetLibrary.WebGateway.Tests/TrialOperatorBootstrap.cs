using System.Text.Json;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// The protected-operator half of a real trial: the key file the operator process protects, and the first
/// administrator account every later step signs in as.
/// </summary>
/// <remarks>
/// Both real trials need exactly this and nothing about it is specific to what either one tests, so it
/// lives in one place: a second copy would be a second definition of what "an initialized trial" means.
/// The trial owns the runtime root it passes in, so the key and the account it creates belong to that
/// trial alone.
/// </remarks>
internal static class TrialOperatorBootstrap
{
    public static async Task InitializeAsync(TrialHostIntegrationFixture host)
    {
        var key = await TrialAdministratorOperator.ExecuteAsync(
            "initialize-key", host.Runtime, Stream.Null, CancellationToken.None);
        Assert.AreEqual(0, key.ExitCode, "Initialize protected operator key");
        var request = JsonSerializer.SerializeToUtf8Bytes(new
        {
            authorization_id = Guid.NewGuid().ToString("D"),
            operation_id = Guid.NewGuid().ToString("D"),
            account_name = "trial-admin",
            display_name = "试用管理员",
            expires_at = DateTimeOffset.UtcNow.AddMinutes(5),
            password = TrialHostIntegrationAuthentication.Password,
        });
        using var input = new MemoryStream(request);
        var administrator = await TrialAdministratorOperator.ExecuteAsync(
            "bootstrap", host.Runtime, input, CancellationToken.None);
        Assert.AreEqual(0, administrator.ExitCode, administrator.Json);
    }
}
