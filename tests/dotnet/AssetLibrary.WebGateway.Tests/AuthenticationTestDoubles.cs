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
