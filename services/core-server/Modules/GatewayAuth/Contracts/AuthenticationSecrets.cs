using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AssetLibrary.Modules.GatewayAuth.Contracts;

public sealed class LocalSecret : IDisposable
{
    public const int MinimumEnrollmentScalars = 15;
    public const int MaximumScalars = 128;
    private char[]? characters;

    public LocalSecret(ReadOnlySpan<char> value)
    {
        var scalarLength = Validate(value);
        characters = value.ToArray();
        ScalarLength = scalarLength;
    }

    public int ScalarLength { get; }

    public bool MeetsEnrollmentLengthPolicy => ScalarLength >= MinimumEnrollmentScalars;

    internal ReadOnlySpan<char> Value => characters
        ?? throw new ObjectDisposedException(nameof(LocalSecret));

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref characters, null);
        if (owned is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(owned.AsSpan()));
    }

    public override string ToString() => "[redacted]";

    private static int Validate(ReadOnlySpan<char> value)
    {
        var remaining = value;
        var count = 0;
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done || Rune.IsControl(rune))
            {
                throw new ArgumentException("A local secret contains invalid characters.", nameof(value));
            }

            count++;
            if (count > MaximumScalars)
            {
                throw new ArgumentException("A local secret is too long.", nameof(value));
            }

            remaining = remaining[consumed..];
        }

        return count;
    }
}

public sealed class BrowserSessionToken : IDisposable
{
    internal const int ByteLength = 32;
    private byte[]? value;

    internal BrowserSessionToken(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != ByteLength)
        {
            throw new ArgumentException("A browser session token has an invalid length.", nameof(value));
        }

        this.value = value;
    }

    internal ReadOnlySpan<byte> Value => value
        ?? throw new ObjectDisposedException(nameof(BrowserSessionToken));

    public static BrowserSessionToken Parse(string encoded) =>
        new(AuthenticationTokenEncoding.Decode(encoded, nameof(encoded)));

    public string Export() => AuthenticationTokenEncoding.Encode(Value);

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref value, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public override string ToString() => "[redacted]";
}

public sealed class BrowserCsrfToken : IDisposable
{
    internal const int ByteLength = 32;
    private byte[]? value;

    internal BrowserCsrfToken(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != ByteLength)
        {
            throw new ArgumentException("A browser CSRF token has an invalid length.", nameof(value));
        }

        this.value = value;
    }

    internal ReadOnlySpan<byte> Value => value
        ?? throw new ObjectDisposedException(nameof(BrowserCsrfToken));

    public static BrowserCsrfToken Parse(string encoded) =>
        new(AuthenticationTokenEncoding.Decode(encoded, nameof(encoded)));

    public string Export() => AuthenticationTokenEncoding.Encode(Value);

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref value, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public override string ToString() => "[redacted]";
}

public sealed class BrowserSessionCredentials : IDisposable
{
    public BrowserSessionCredentials(
        BrowserSessionToken sessionToken,
        BrowserCsrfToken csrfToken,
        DateTimeOffset issuedAt,
        DateTimeOffset idleExpiresAt,
        DateTimeOffset absoluteExpiresAt)
    {
        SessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
        CsrfToken = csrfToken ?? throw new ArgumentNullException(nameof(csrfToken));
        if (issuedAt > idleExpiresAt || idleExpiresAt > absoluteExpiresAt)
        {
            throw new ArgumentException("Browser session timestamps are invalid.");
        }

        IssuedAt = issuedAt;
        IdleExpiresAt = idleExpiresAt;
        AbsoluteExpiresAt = absoluteExpiresAt;
    }

    public BrowserSessionToken SessionToken { get; }

    public BrowserCsrfToken CsrfToken { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset IdleExpiresAt { get; }

    public DateTimeOffset AbsoluteExpiresAt { get; }

    public void Dispose()
    {
        SessionToken.Dispose();
        CsrfToken.Dispose();
    }

    public override string ToString() => "[redacted]";
}

internal static class AuthenticationTokenEncoding
{
    private const int EncodedLength = 43;

    public static string Encode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string encoded, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length != EncodedLength
            || encoded.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_'))
        {
            throw new ArgumentException("An authentication token is invalid.", parameterName);
        }

        try
        {
            var decoded = Convert.FromBase64String(
                encoded.Replace('-', '+').Replace('_', '/') + "=");
            if (decoded.Length != BrowserSessionToken.ByteLength
                || !string.Equals(Encode(decoded), encoded, StringComparison.Ordinal))
            {
                CryptographicOperations.ZeroMemory(decoded);
                throw new ArgumentException("An authentication token is invalid.", parameterName);
            }

            return decoded;
        }
        catch (FormatException error)
        {
            throw new ArgumentException("An authentication token is invalid.", parameterName, error);
        }
    }
}
