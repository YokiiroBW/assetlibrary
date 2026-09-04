using System.Security.Cryptography;
using System.Text;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal sealed class Pbkdf2LocalCredentialDeriver : ILocalCredentialDeriver
{
    public LocalCredentialEnrollmentMaterial Derive(LocalSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var salt = RandomNumberGenerator.GetBytes(LocalSecretHashingPolicy.SaltBytes);
        var encoded = new byte[Encoding.UTF8.GetByteCount(secret.Value)];
        _ = Encoding.UTF8.GetBytes(secret.Value, encoded);
        var digest = new byte[LocalSecretHashingPolicy.DigestBytes];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(
                encoded,
                salt,
                digest,
                LocalSecretHashingPolicy.MinimumIterations,
                HashAlgorithmName.SHA256);
            return new LocalCredentialEnrollmentMaterial(
                LocalSecretHashingPolicy.Algorithm,
                LocalSecretHashingPolicy.MinimumIterations,
                salt,
                digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(encoded);
            CryptographicOperations.ZeroMemory(digest);
        }
    }
}
