using System.Security.Cryptography;
using System.Text;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class LocalAuthenticationTests
{
    [TestMethod]
    public void AccountNamesAreCanonicalAndBounded()
    {
        Assert.AreEqual("asset.user-1", new LocalAccountName("Asset.User-1").Value);

        foreach (var invalid in new[]
                 {
                     "ab",
                     ".asset",
                     "asset_",
                     "asset user",
                     "资产用户",
                     " asset",
                     new string('a', LocalAccountName.MaximumLength + 1),
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(() => _ = new LocalAccountName(invalid));
        }
    }

    [TestMethod]
    public void LocalSecretsUseUnicodeScalarBoundsAndRedactStringification()
    {
        using var shortSecret = new LocalSecret("short".AsSpan());
        using var eligible = new LocalSecret("十二三四五六七八九十abcde😀".AsSpan());

        Assert.IsFalse(shortSecret.MeetsEnrollmentLengthPolicy);
        Assert.IsTrue(eligible.MeetsEnrollmentLengthPolicy);
        Assert.AreEqual("[redacted]", eligible.ToString());
        Assert.ThrowsExactly<ArgumentException>(
            () => _ = new LocalSecret(new string('a', LocalSecret.MaximumScalars + 1).AsSpan()));
        Assert.ThrowsExactly<ArgumentException>(() => _ = new LocalSecret("line\nbreak".AsSpan()));
        Assert.ThrowsExactly<ArgumentException>(() => _ = new LocalSecret("\ud800".AsSpan()));
    }

    [TestMethod]
    public void OpaqueTokensRoundTripCanonicallyAndNeverStringifyTheirValue()
    {
        var bytes = Enumerable.Range(0, BrowserSessionToken.ByteLength)
            .Select(value => (byte)value)
            .ToArray();
        using var token = new BrowserSessionToken(bytes);
        var encoded = token.Export();
        using var parsed = BrowserSessionToken.Parse(encoded);

        Assert.HasCount(43, encoded);
        CollectionAssert.AreEqual(bytes, parsed.Value.ToArray());
        Assert.AreEqual("[redacted]", token.ToString());
        Assert.ThrowsExactly<ArgumentException>(() => BrowserSessionToken.Parse(encoded + "="));
        Assert.ThrowsExactly<ArgumentException>(() => BrowserSessionToken.Parse(new string('a', 43)));
    }

    [TestMethod]
    public void Pbkdf2VerifierAcceptsOnlyTheMatchingSecretAtTheFrozenCost()
    {
        using var secret = new LocalSecret("correct horse battery staple".AsSpan());
        using var wrong = new LocalSecret("incorrect horse battery staple".AsSpan());
        var salt = Enumerable.Range(1, LocalSecretHashingPolicy.SaltBytes)
            .Select(value => (byte)value)
            .ToArray();
        var encoded = new byte[Encoding.UTF8.GetByteCount(secret.Value)];
        _ = Encoding.UTF8.GetBytes(secret.Value, encoded);
        var digest = Rfc2898DeriveBytes.Pbkdf2(
            encoded,
            salt,
            LocalSecretHashingPolicy.MinimumIterations,
            HashAlgorithmName.SHA256,
            LocalSecretHashingPolicy.DigestBytes);
        var verifier = new Pbkdf2LocalSecretVerifier();

        Assert.IsTrue(verifier.Verify(
            secret,
            LocalSecretHashingPolicy.MinimumIterations,
            salt,
            digest));
        Assert.IsFalse(verifier.Verify(
            wrong,
            LocalSecretHashingPolicy.MinimumIterations,
            salt,
            digest));
        Assert.ThrowsExactly<ArgumentException>(() => verifier.Verify(
            secret,
            LocalSecretHashingPolicy.MinimumIterations - 1,
            salt,
            digest));
        CryptographicOperations.ZeroMemory(encoded);
        CryptographicOperations.ZeroMemory(digest);
    }
}

[TestClass]
public sealed class LocalSignInServiceTests
{
    [TestMethod]
    public async Task SuccessfulLocalSignInIssuesASeparateServerSession()
    {
        var credentials = new FakeLocalCredentialStore
        {
            Find = () => AuthenticationTestData.Credential(),
        };
        var sessionStore = new FakeBrowserSessionStore();
        var generator = new FakeAuthenticationSecretGenerator();
        var issuer = new BrowserSessionIssuer(
            sessionStore,
            generator,
            NullLogger<BrowserSessionIssuer>.Instance);
        var verifier = new FakeLocalSecretVerifier(result: true);
        using var service = new LocalAuthenticationService(
            credentials,
            issuer,
            verifier,
            NullLogger<LocalAuthenticationService>.Instance);
        using var secret = new LocalSecret("valid sign in secret".AsSpan());

        var result = await service.SignInAsync(
            new LocalAccountName("test-user"),
            secret,
            CancellationToken.None);

        Assert.AreEqual(LocalSignInStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.Session);
        Assert.AreEqual("[redacted]", result.Session.ToString());
        Assert.AreNotEqual(result.Session.SessionToken.Export(), result.Session.CsrfToken.Export());
        Assert.AreEqual(1, verifier.Calls);
        Assert.AreEqual(0, credentials.FailureCount);
        Assert.HasCount(1, sessionStore.CreatedIdentities);
        CollectionAssert.AreEqual(
            SHA256.HashData(generator.Issued[0]),
            sessionStore.CreatedSessionDigests.Single());
        CollectionAssert.AreEqual(
            SHA256.HashData(generator.Issued[1]),
            sessionStore.CreatedCsrfDigests.Single());
        result.Session.Dispose();
    }

    [TestMethod]
    public async Task WrongAndUnknownAccountsShareTheRejectedResultAndVerificationWork()
    {
        var existingStore = new FakeLocalCredentialStore
        {
            Find = () => AuthenticationTestData.Credential(),
        };
        var unknownStore = new FakeLocalCredentialStore();
        var sessionStore = new FakeBrowserSessionStore();
        var verifier = new FakeLocalSecretVerifier(result: false);
        var issuer = new BrowserSessionIssuer(
            sessionStore,
            NullLogger<BrowserSessionIssuer>.Instance);
        using var existing = new LocalAuthenticationService(
            existingStore,
            issuer,
            verifier,
            NullLogger<LocalAuthenticationService>.Instance);
        using var unknown = new LocalAuthenticationService(
            unknownStore,
            issuer,
            verifier,
            NullLogger<LocalAuthenticationService>.Instance);
        using var secret = new LocalSecret("wrong".AsSpan());

        var existingResult = await existing.SignInAsync(
            new LocalAccountName("test-user"),
            secret,
            CancellationToken.None);
        var unknownResult = await unknown.SignInAsync(
            new LocalAccountName("unknown-user"),
            secret,
            CancellationToken.None);

        Assert.AreEqual(LocalSignInResult.Rejected(), existingResult);
        Assert.AreEqual(LocalSignInResult.Rejected(), unknownResult);
        Assert.AreEqual(2, verifier.Calls);
        Assert.AreEqual(1, existingStore.FailureCount);
        Assert.AreEqual(7L, existingStore.LastFailureVersion);
        Assert.AreEqual(0, unknownStore.FailureCount);
        Assert.IsEmpty(sessionStore.CreatedIdentities);
    }

    [TestMethod]
    public async Task ActiveBackoffRejectsEvenACorrectSecretWithoutExtendingTheWindow()
    {
        var credentials = new FakeLocalCredentialStore
        {
            Find = () => AuthenticationTestData.Credential(canAttempt: false),
        };
        var sessionStore = new FakeBrowserSessionStore();
        var verifier = new FakeLocalSecretVerifier(result: true);
        using var service = new LocalAuthenticationService(
            credentials,
            new BrowserSessionIssuer(sessionStore, NullLogger<BrowserSessionIssuer>.Instance),
            verifier,
            NullLogger<LocalAuthenticationService>.Instance);
        using var secret = new LocalSecret("correct".AsSpan());

        var result = await service.SignInAsync(
            new LocalAccountName("test-user"),
            secret,
            CancellationToken.None);

        Assert.AreEqual(LocalSignInResult.Rejected(), result);
        Assert.AreEqual(1, verifier.Calls);
        Assert.AreEqual(0, credentials.FailureCount);
        Assert.IsEmpty(sessionStore.CreatedIdentities);
    }
}

[TestClass]
public sealed class SessionIssuanceTests
{
    [TestMethod]
    public void CreatedSessionResultRejectsPolicyBreakingTimestamps()
    {
        var issuedAt = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

        Assert.ThrowsExactly<ArgumentException>(() => BrowserSessionCreateResult.Created(
            issuedAt,
            issuedAt.AddMinutes(31),
            issuedAt.AddHours(12)));
        Assert.ThrowsExactly<ArgumentException>(() => BrowserSessionCreateResult.Created(
            issuedAt,
            issuedAt.AddMinutes(30),
            issuedAt.AddHours(13)));
    }

    [TestMethod]
    public async Task StaleIdentityDisposesGeneratedSecretsAndReturnsGenericRejection()
    {
        var sessionStore = new FakeBrowserSessionStore();
        sessionStore.Enqueue(BrowserSessionCreateResult.IdentityStale());
        var generator = new FakeAuthenticationSecretGenerator();
        var issuer = new BrowserSessionIssuer(
            sessionStore,
            generator,
            NullLogger<BrowserSessionIssuer>.Instance);
        using var service = new LocalAuthenticationService(
            new FakeLocalCredentialStore { Find = () => AuthenticationTestData.Credential() },
            issuer,
            new FakeLocalSecretVerifier(result: true),
            NullLogger<LocalAuthenticationService>.Instance);
        using var secret = new LocalSecret("correct".AsSpan());

        var result = await service.SignInAsync(
            new LocalAccountName("test-user"),
            secret,
            CancellationToken.None);

        Assert.AreEqual(LocalSignInResult.Rejected(), result);
        Assert.IsTrue(generator.Issued.All(value => value.All(item => item == 0)));
    }

    [TestMethod]
    public async Task TokenCollisionRetriesWithFreshSecretsAndOidcUsesTheSameIssuerBoundary()
    {
        var sessionStore = new FakeBrowserSessionStore();
        sessionStore.Enqueue(BrowserSessionCreateResult.TokenConflict());
        sessionStore.Enqueue(AuthenticationTestData.Created());
        var generator = new FakeAuthenticationSecretGenerator();
        var issuer = new BrowserSessionIssuer(
            sessionStore,
            generator,
            NullLogger<BrowserSessionIssuer>.Instance);

        using var local = await issuer.IssueAsync(
            AuthenticationTestData.Verified(),
            CancellationToken.None);
        using var oidc = await issuer.IssueAsync(
            AuthenticationTestData.Verified(PrimaryAuthenticationMethod.Oidc),
            CancellationToken.None);

        Assert.IsNotNull(local);
        Assert.IsNotNull(oidc);
        Assert.HasCount(3, sessionStore.CreatedIdentities);
        Assert.AreEqual(
            PrimaryAuthenticationMethod.Oidc,
            sessionStore.CreatedIdentities[^1].Identity.AuthenticationMethod);
        Assert.IsNull(sessionStore.CreatedIdentities[^1].LocalCredentialVersion);
        Assert.IsTrue(generator.Issued[0].All(value => value == 0));
        Assert.IsTrue(generator.Issued[1].All(value => value == 0));
    }
}

[TestClass]
public sealed class AuthenticationCancellationTests
{
    [TestMethod]
    public async Task CallerCancellationIsNotConvertedIntoAnAuthenticationTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var service = new LocalAuthenticationService(
            new FakeLocalCredentialStore(),
            new BrowserSessionIssuer(
                new FakeBrowserSessionStore(),
                NullLogger<BrowserSessionIssuer>.Instance),
            NullLogger<LocalAuthenticationService>.Instance);
        using var secret = new LocalSecret("attempt".AsSpan());

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await service.SignInAsync(
                new LocalAccountName("test-user"),
                secret,
                cancellation.Token));
    }
}

[TestClass]
public sealed class BrowserSessionServiceTests
{
    [TestMethod]
    public async Task SessionAuthenticationHashesTokensAndRequiresCsrfForMutationsAndLogout()
    {
        var store = new FakeBrowserSessionStore
        {
            AuthenticationResult = AuthenticationTestData.Identity(),
            RevokeResult = true,
        };
        var service = new BrowserSessionService(
            store,
            NullLogger<BrowserSessionService>.Instance);
        var sessionBytes = Enumerable.Repeat((byte)0xA1, BrowserSessionToken.ByteLength).ToArray();
        var csrfBytes = Enumerable.Repeat((byte)0xB2, BrowserCsrfToken.ByteLength).ToArray();
        using var session = new BrowserSessionToken(sessionBytes);
        using var csrf = new BrowserCsrfToken(csrfBytes);

        var read = await service.AuthenticateForReadAsync(session, CancellationToken.None);
        Assert.IsTrue(read.IsAuthenticated);
        Assert.IsFalse(store.LastRequireCsrf ?? true);
        Assert.IsNull(store.LastAuthenticationCsrfDigest);
        CollectionAssert.AreEqual(SHA256.HashData(sessionBytes), store.LastAuthenticationSessionDigest);

        var mutation = await service.AuthenticateForMutationAsync(
            session,
            csrf,
            CancellationToken.None);
        Assert.IsTrue(mutation.IsAuthenticated);
        Assert.IsTrue(store.LastRequireCsrf ?? false);
        CollectionAssert.AreEqual(SHA256.HashData(csrfBytes), store.LastAuthenticationCsrfDigest);

        Assert.IsTrue(await service.SignOutAsync(session, csrf, CancellationToken.None));
        CollectionAssert.AreEqual(SHA256.HashData(sessionBytes), store.LastRevocationSessionDigest);
        CollectionAssert.AreEqual(SHA256.HashData(csrfBytes), store.LastRevocationCsrfDigest);
    }

}
