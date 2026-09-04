using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;

namespace AssetLibrary.WebGateway.Tests;

internal static class AuthenticationTestData
{
    public static readonly Guid PrincipalId = Guid.Parse("b7d51fa8-418f-4e0c-9702-032daf323d7c");

    public static AuthenticatedIdentity Identity(
        PrimaryAuthenticationMethod method = PrimaryAuthenticationMethod.LocalAccount) =>
        new(
            PrincipalId,
            new AuthenticatedSubject(method == PrimaryAuthenticationMethod.LocalAccount
                ? "local:test-user"
                : "oidc:test-user"),
            "Test user",
            isSystemAdministrator: false,
            principalSessionVersion: 3,
            method);

    public static AuthenticatedIdentity Administrator(long principalSessionVersion = 3) =>
        new(
            PrincipalId,
            new AuthenticatedSubject("local:administrator"),
            "Test administrator",
            isSystemAdministrator: true,
            principalSessionVersion,
            PrimaryAuthenticationMethod.LocalAccount);

    public static LocalAccountState Account(
        string accountName = "managed-user",
        bool isSystemAdministrator = false,
        bool isEnabled = true,
        long credentialVersion = 1,
        long principalSessionVersion = 1) =>
        new(
            Guid.Parse("9f5fd8a7-1a93-413d-9319-d19bf0e36c72"),
            new LocalAccountName(accountName),
            new LocalAccountDisplayName("Managed user"),
            isSystemAdministrator,
            isEnabled,
            credentialVersion,
            principalSessionVersion);

    public static VerifiedPrimaryIdentity Verified(
        PrimaryAuthenticationMethod method = PrimaryAuthenticationMethod.LocalAccount) =>
        new(Identity(method), method == PrimaryAuthenticationMethod.LocalAccount ? 7 : null);

    public static LocalCredentialMaterial Credential(bool canAttempt = true) =>
        new(
            Verified(),
            LocalSecretHashingPolicy.Algorithm,
            LocalSecretHashingPolicy.MinimumIterations,
            Enumerable.Repeat((byte)0x31, LocalSecretHashingPolicy.SaltBytes).ToArray(),
            Enumerable.Repeat((byte)0x52, LocalSecretHashingPolicy.DigestBytes).ToArray(),
            canAttempt);

    public static BrowserSessionCreateResult Created() =>
        BrowserSessionCreateResult.Created(
            new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 4, 12, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero));
}

internal sealed class FakeLocalSecretRiskChecker(LocalSecretRisk result)
    : ILocalSecretRiskChecker
{
    public int Calls { get; private set; }

    public Func<CancellationToken, ValueTask<LocalSecretRisk>>? Evaluate { get; set; }

    public ValueTask<LocalSecretRisk> EvaluateAsync(
        LocalSecret secret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Evaluate?.Invoke(cancellationToken) ?? ValueTask.FromResult(result);
    }
}

internal sealed class FakeLocalCredentialDeriver : ILocalCredentialDeriver
{
    public int Calls { get; private set; }

    public LocalCredentialEnrollmentMaterial? LastMaterial { get; private set; }

    public LocalCredentialEnrollmentMaterial Derive(LocalSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        Calls++;
        LastMaterial = new LocalCredentialEnrollmentMaterial(
            LocalSecretHashingPolicy.Algorithm,
            LocalSecretHashingPolicy.MinimumIterations,
            Enumerable.Repeat((byte)0x41, LocalSecretHashingPolicy.SaltBytes).ToArray(),
            Enumerable.Repeat((byte)0x52, LocalSecretHashingPolicy.DigestBytes).ToArray());
        return LastMaterial;
    }
}

internal sealed class FakeLocalAccountLifecycleStore : ILocalAccountLifecycleStore
{
    public LocalAccountLifecycleResult Result { get; set; } =
        new(LocalAccountLifecycleOutcome.Applied, false, AuthenticationTestData.Account());

    public int FindCalls { get; private set; }

    public int ProvisionCalls { get; private set; }

    public int ReplaceCalls { get; private set; }

    public int SetEnabledCalls { get; private set; }

    public Guid LastOperationId { get; private set; }

    public LocalAccountName LastAccountName { get; private set; }

    public long LastExpectedVersion { get; private set; }

    public bool LastBoolean { get; private set; }

    public Func<CancellationToken, ValueTask<LocalAccountLifecycleResult>>? Execute { get; set; }

    public ValueTask<LocalAccountLifecycleResult> FindAsync(
        AuthenticatedIdentity actor,
        LocalAccountName accountName,
        CancellationToken cancellationToken)
    {
        FindCalls++;
        LastAccountName = accountName;
        return Complete(cancellationToken);
    }

    public ValueTask<LocalAccountLifecycleResult> ProvisionAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        Guid requestedPrincipalId,
        LocalAccountName accountName,
        LocalAccountDisplayName displayName,
        bool isSystemAdministrator,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken)
    {
        AssertMaterial(credential);
        Assert.AreNotEqual(Guid.Empty, requestedPrincipalId);
        Assert.AreEqual("Managed user", displayName.Value);
        ProvisionCalls++;
        LastOperationId = operationId;
        LastAccountName = accountName;
        LastBoolean = isSystemAdministrator;
        return Complete(cancellationToken);
    }

    public ValueTask<LocalAccountLifecycleResult> ReplaceCredentialAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedCredentialVersion,
        bool enableAccount,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken)
    {
        AssertMaterial(credential);
        ReplaceCalls++;
        LastOperationId = operationId;
        LastAccountName = accountName;
        LastExpectedVersion = expectedCredentialVersion;
        LastBoolean = enableAccount;
        return Complete(cancellationToken);
    }

    public ValueTask<LocalAccountLifecycleResult> SetEnabledAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedPrincipalSessionVersion,
        bool enabled,
        CancellationToken cancellationToken)
    {
        SetEnabledCalls++;
        LastOperationId = operationId;
        LastAccountName = accountName;
        LastExpectedVersion = expectedPrincipalSessionVersion;
        LastBoolean = enabled;
        return Complete(cancellationToken);
    }

    private ValueTask<LocalAccountLifecycleResult> Complete(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Execute?.Invoke(cancellationToken) ?? ValueTask.FromResult(Result);
    }

    private static void AssertMaterial(LocalCredentialEnrollmentMaterial credential)
    {
        Assert.AreEqual(LocalSecretHashingPolicy.Algorithm, credential.Algorithm);
        Assert.AreEqual(LocalSecretHashingPolicy.MinimumIterations, credential.Iterations);
        Assert.AreEqual(LocalSecretHashingPolicy.SaltBytes, credential.Salt.Length);
        Assert.AreEqual(LocalSecretHashingPolicy.DigestBytes, credential.Digest.Length);
    }
}

internal sealed class FakeLocalCredentialStore : ILocalCredentialStore
{
    public Func<LocalCredentialMaterial?> Find { get; set; } = () => null;

    public int FindCount { get; private set; }

    public int FailureCount { get; private set; }

    public long? LastFailureVersion { get; private set; }

    public ValueTask<LocalCredentialMaterial?> FindAsync(
        LocalAccountName accountName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FindCount++;
        return ValueTask.FromResult(Find());
    }

    public ValueTask RecordFailureAsync(
        LocalAccountName accountName,
        long observedCredentialVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FailureCount++;
        LastFailureVersion = observedCredentialVersion;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeBrowserSessionStore : IBrowserSessionStore
{
    private readonly Queue<BrowserSessionCreateResult> createResults = new();

    public List<byte[]> CreatedSessionDigests { get; } = [];

    public List<byte[]> CreatedCsrfDigests { get; } = [];

    public List<VerifiedPrimaryIdentity> CreatedIdentities { get; } = [];

    public AuthenticatedIdentity? AuthenticationResult { get; set; }

    public bool RevokeResult { get; set; }

    public bool? LastRequireCsrf { get; private set; }

    public byte[]? LastAuthenticationSessionDigest { get; private set; }

    public byte[]? LastAuthenticationCsrfDigest { get; private set; }

    public byte[]? LastRevocationSessionDigest { get; private set; }

    public byte[]? LastRevocationCsrfDigest { get; private set; }

    public void Enqueue(BrowserSessionCreateResult result) => createResults.Enqueue(result);

    public ValueTask<BrowserSessionCreateResult> CreateAsync(
        VerifiedPrimaryIdentity identity,
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest csrfDigest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CreatedIdentities.Add(identity);
        CreatedSessionDigests.Add(sessionDigest.Value.ToArray());
        CreatedCsrfDigests.Add(csrfDigest.Value.ToArray());
        return ValueTask.FromResult(createResults.Count == 0
            ? AuthenticationTestData.Created()
            : createResults.Dequeue());
    }

    public ValueTask<AuthenticatedIdentity?> AuthenticateAsync(
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest? csrfDigest,
        bool requireCsrf,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequireCsrf = requireCsrf;
        LastAuthenticationSessionDigest = sessionDigest.Value.ToArray();
        LastAuthenticationCsrfDigest = csrfDigest?.Value.ToArray();
        return ValueTask.FromResult(AuthenticationResult);
    }

    public ValueTask<bool> RevokeAsync(
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest csrfDigest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRevocationSessionDigest = sessionDigest.Value.ToArray();
        LastRevocationCsrfDigest = csrfDigest.Value.ToArray();
        return ValueTask.FromResult(RevokeResult);
    }
}

internal sealed class FakeLocalSecretVerifier(bool result) : ILocalSecretVerifier
{
    public int Calls { get; private set; }

    public bool Verify(
        LocalSecret secret,
        int iterations,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> expectedDigest)
    {
        Calls++;
        return result;
    }
}

internal sealed class FakeAuthenticationSecretGenerator : IAuthenticationSecretGenerator
{
    private byte next = 1;

    public List<byte[]> Issued { get; } = [];

    public byte[] Generate(int byteLength)
    {
        var value = Enumerable.Repeat(next++, byteLength).ToArray();
        Issued.Add(value);
        return value;
    }
}
