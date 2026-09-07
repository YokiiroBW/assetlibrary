using System.Text;
using System.Text.Json;
using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class TrialAuthenticationOperatorTests
{
    [TestMethod]
    public async Task OperatorInputKeepsExplicitRetryIdentityAndNeverStringifiesSecrets()
    {
        var authorization = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            authorization_id = authorization.ToString("D"),
            operation_id = operation.ToString("D"),
            account_name = "trial-admin",
            display_name = "Trial administrator",
            expires_at = expires,
            password = TrialAuthenticationStore.Password,
        });
        using var input = new MemoryStream(bytes);
        using var parsed = await TrialOperatorInput.ReadAsync("bootstrap", input, CancellationToken.None);
        Assert.AreEqual(authorization, parsed.Request.AuthorizationId);
        Assert.AreEqual(operation, parsed.Request.OperationId);
        Assert.AreEqual(expires, parsed.Request.ExpiresAt);
        Assert.AreEqual(AdministratorBootstrapRecoveryAction.BootstrapFirstAdministrator, parsed.Request.Action);
        Assert.AreEqual("[redacted]", parsed.ToString());
        Assert.IsTrue(parsed.Secret.MeetsEnrollmentLengthPolicy);
    }

    [TestMethod]
    public async Task OperatorInputRejectsOversizeAmbiguousAndMissingFields()
    {
        using var oversized = new MemoryStream(new byte[16385]);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            TrialOperatorInput.ReadAsync("recover", oversized, CancellationToken.None).AsTask());
        foreach (var json in new[] { "[]", "{}", "{\"authorization_id\":\"x\",\"authorization_id\":\"x\"}" })
        {
            using var malformed = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await Assert.ThrowsExactlyAsync<JsonException>(() =>
                TrialOperatorInput.ReadAsync("recover", malformed, CancellationToken.None).AsTask());
        }
    }

    [TestMethod]
    public async Task CancelledOperatorInputPropagatesCancellation()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            TrialOperatorInput.ReadAsync("recover", input, cancelled.Token).AsTask());
    }
}
