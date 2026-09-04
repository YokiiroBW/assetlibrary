using System.Security.Cryptography;
using System.Text;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Domain;

public static class LocalSecretHashingPolicy
{
    public const string Algorithm = "pbkdf2-sha256";
    public const int MinimumIterations = 600_000;
    public const int MaximumIterations = 2_000_000;
    public const int SaltBytes = 16;
    public const int DigestBytes = 32;
}

internal interface ILocalSecretVerifier
{
    bool Verify(
        LocalSecret secret,
        int iterations,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> expectedDigest);
}

internal sealed class Pbkdf2LocalSecretVerifier : ILocalSecretVerifier
{
    public bool Verify(
        LocalSecret secret,
        int iterations,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> expectedDigest)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (iterations is < LocalSecretHashingPolicy.MinimumIterations
                or > LocalSecretHashingPolicy.MaximumIterations
            || salt.Length is < LocalSecretHashingPolicy.SaltBytes or > 64
            || expectedDigest.Length != LocalSecretHashingPolicy.DigestBytes)
        {
            throw new ArgumentException("Local credential material is invalid.");
        }

        var encoded = new byte[Encoding.UTF8.GetByteCount(secret.Value)];
        _ = Encoding.UTF8.GetBytes(secret.Value, encoded);
        Span<byte> actual = stackalloc byte[LocalSecretHashingPolicy.DigestBytes];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(
                encoded,
                salt,
                actual,
                iterations,
                HashAlgorithmName.SHA256);
            return CryptographicOperations.FixedTimeEquals(actual, expectedDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
            CryptographicOperations.ZeroMemory(actual);
        }
    }
}
