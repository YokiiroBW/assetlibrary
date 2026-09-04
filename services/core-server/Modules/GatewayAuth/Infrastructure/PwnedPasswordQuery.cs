using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PwnedPasswordQuery : IDisposable
{
    public const int PrefixCharacters = 5;
    public const int SuffixCharacters = 35;
    private const int HashNibbles = PrefixCharacters + SuffixCharacters;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private char[]? suffix;

    private PwnedPasswordQuery(string prefix, char[] suffix)
    {
        Prefix = prefix;
        this.suffix = suffix;
    }

    public string Prefix { get; }

    public ReadOnlySpan<char> Suffix => suffix
        ?? throw new ObjectDisposedException(nameof(PwnedPasswordQuery));

    [SuppressMessage(
        "Security",
        "CA5350:Do not use weak cryptographic algorithms",
        Justification = "HIBP range lookup requires SHA-1 for k-anonymity compatibility; credential derivation remains PBKDF2-SHA256.")]
    public static PwnedPasswordQuery Create(LocalSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var encodedLength = StrictUtf8.GetByteCount(secret.Value);
        var encoded = ArrayPool<byte>.Shared.Rent(encodedLength);
        Span<byte> digest = stackalloc byte[SHA1.HashSizeInBytes];
        Span<char> prefix = stackalloc char[PrefixCharacters];
        var suffix = new char[SuffixCharacters];
        try
        {
            var written = StrictUtf8.GetBytes(secret.Value, encoded);
            SHA1.HashData(encoded.AsSpan(0, written), digest);
            for (var nibble = 0; nibble < HashNibbles; nibble++)
            {
                var value = (digest[nibble / 2] >> (nibble % 2 == 0 ? 4 : 0)) & 0x0f;
                var character = value < 10 ? (char)('0' + value) : (char)('A' + value - 10);
                if (nibble < PrefixCharacters)
                {
                    prefix[nibble] = character;
                }
                else
                {
                    suffix[nibble - PrefixCharacters] = character;
                }
            }

            return new PwnedPasswordQuery(new string(prefix), suffix);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(suffix.AsSpan()));
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
            ArrayPool<byte>.Shared.Return(encoded);
            CryptographicOperations.ZeroMemory(digest);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(prefix));
        }
    }

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref suffix, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(owned.AsSpan()));
        }
    }
}
