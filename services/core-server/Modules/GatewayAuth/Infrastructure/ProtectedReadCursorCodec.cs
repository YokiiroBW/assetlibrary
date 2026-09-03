using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class ProtectedReadCursorCodec(IDataProtectionProvider protectionProvider)
{
    private const int CurrentVersion = 1;
    private const int MaximumPlaintextBytes = 6144;
    private readonly IDataProtector protector = (protectionProvider
        ?? throw new ArgumentNullException(nameof(protectionProvider)))
        .CreateProtector("AssetLibrary.GatewayAuth.ReadCursor.v1");

    public ReadPageCursor Encode(
        string scope,
        string filter,
        string sortName,
        Guid? libraryId,
        Guid? entryId)
    {
        var payload = new CursorPayload(
            CurrentVersion,
            scope,
            Fingerprint(filter),
            sortName,
            libraryId,
            entryId);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (plaintext.Length > MaximumPlaintextBytes)
        {
            throw new InvalidOperationException("The read cursor payload exceeded its storage bound.");
        }

        return new ReadPageCursor(WebEncoders.Base64UrlEncode(protector.Protect(plaintext)));
    }

    public DecodedReadCursor Decode(ReadPageCursor cursor, string expectedScope, string expectedFilter)
    {
        try
        {
            var protectedBytes = WebEncoders.Base64UrlDecode(cursor.Value);
            var plaintext = protector.Unprotect(protectedBytes);
            if (plaintext.Length > MaximumPlaintextBytes)
            {
                throw new InvalidReadCursorException();
            }

            var payload = JsonSerializer.Deserialize<CursorPayload>(plaintext)
                ?? throw new InvalidReadCursorException();
            if (payload.Version != CurrentVersion
                || !string.Equals(payload.Scope, expectedScope, StringComparison.Ordinal)
                || !FixedTimeEquals(payload.FilterFingerprint, Fingerprint(expectedFilter))
                || string.IsNullOrEmpty(payload.SortName)
                || payload.SortName.Length > 4096)
            {
                throw new InvalidReadCursorException();
            }

            return new DecodedReadCursor(payload.SortName, payload.LibraryId, payload.EntryId);
        }
        catch (InvalidReadCursorException)
        {
            throw;
        }
        catch (Exception error) when (error is CryptographicException
            or FormatException
            or JsonException
            or ArgumentException)
        {
            throw new InvalidReadCursorException();
        }
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedTimeEquals(string? left, string right)
    {
        if (left is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }

    private sealed record CursorPayload(
        int Version,
        string Scope,
        string FilterFingerprint,
        string SortName,
        Guid? LibraryId,
        Guid? EntryId);
}

internal sealed record DecodedReadCursor(string SortName, Guid? LibraryId, Guid? EntryId);

public sealed class InvalidReadCursorException : ArgumentException
{
    public InvalidReadCursorException()
        : base("The read-page cursor is invalid or no longer applies to this query.")
    {
    }
}
