using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Infrastructure;

/// <summary>
/// Strict raw-byte manifest reader built on <see cref="JsonDocument"/>. The digest is computed over
/// exactly the received bytes: no LF normalization, no reserialization, no BOM skipping. On top of the
/// framework parser it enforces the four decisions the candidate contract freezes and a serializer
/// hides: no duplicate key at any depth, no unknown key, no non-integer number lexeme, and no trailing
/// content. Shape and cross-field policy are then applied before any caller may touch a directory or a
/// file.
/// </summary>
public sealed class MediaPackageManifestReader(MediaPackageInspectionLimits limits)
    : IMediaPackageManifestReader
{
    private const int MaximumNestingDepth = 16;

    /// <summary>
    /// Integer members whose exact token text is part of the contract: a fractional, exponent,
    /// leading-zero, quoted or out-of-range spelling is refused rather than coerced.
    /// </summary>
    private static readonly string[] IntegerFields =
    [
        "schema_version",
        "size_bytes",
        "episode_number",
    ];

    public MediaPackageManifestReadResult Read(
        byte[] manifestBytes,
        Sha256Digest expectedDigest)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        if (manifestBytes.Length == 0 || manifestBytes.Length > limits.MaximumManifestByteCount)
        {
            return Failure(Digest(manifestBytes), "budget_exceeded", null);
        }

        var digest = Digest(manifestBytes);
        if (digest != expectedDigest)
        {
            return Failure(digest, "digest_mismatch", null);
        }

        var issues = new MediaPackageIssueSink(limits.MaximumIssueCount);
        if (!TryDecodeUtf8(manifestBytes, out var text))
        {
            issues.Record("invalid_manifest");
            return Failure(digest, null, issues);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                text,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaximumNestingDepth,
                });
        }
        catch (JsonException)
        {
            // A parser failure is a manifest verdict, never a leaked exception.
            issues.Record("invalid_manifest");
            return Failure(digest, null, issues);
        }

        using (document)
        {
            var root = document.RootElement;

            // A JSON escape may spell half of a UTF-16 surrogate pair. The parser accepts it because it is
            // syntactically legal JSON, but materializing any string then throws, so it is refused first,
            // as a manifest verdict: the exception type and the offending text never reach a report. This
            // check runs on the raw bytes before any member name or value is read, because reading either
            // one is already what throws.
            if (!HasWellFormedStringTokens(text))
            {
                issues.Record("invalid_manifest", "surrogate_escape");
                return Failure(digest, null, issues);
            }

            if (!HasUniqueKeys(root))
            {
                issues.Record("invalid_manifest", "duplicate_key");
                return Failure(digest, null, issues);
            }

            if (!HasIntegerLexemesOnly(root))
            {
                issues.Record("invalid_manifest", "number_lexeme");
                return Failure(digest, null, issues);
            }

            var manifest = MediaPackagePolicy.Validate(root, issues);
            return manifest is null
                ? Failure(digest, null, issues)
                : new MediaPackageManifestReadResult(manifest, digest, null, issues.Issues, issues.IsTruncated);
        }
    }

    private static Sha256Digest Digest(byte[] manifestBytes) =>
        new(Convert.ToHexStringLower(SHA256.HashData(manifestBytes)));

    private static MediaPackageManifestReadResult Failure(
        Sha256Digest digest,
        string? code,
        MediaPackageIssueSink? issues) =>
        new(
            null,
            digest,
            code,
            issues is null ? [] : [.. issues.Issues],
            issues?.IsTruncated ?? false);

    /// <summary>
    /// Rejects a byte order mark and any invalid UTF-8 sequence, including overlong three- and
    /// four-byte forms and UTF-16 surrogate halves that a lenient decoder would replace instead of
    /// refusing.
    /// </summary>
    private static bool TryDecodeUtf8(byte[] bytes, out string text)
    {
        text = string.Empty;
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            return false;
        }

        var index = 0;
        while (index < bytes.Length)
        {
            var lead = bytes[index];
            if (lead < 0x80)
            {
                index++;
                continue;
            }

            var length = SequenceLength(lead);
            if (length == 0 || index + length > bytes.Length)
            {
                return false;
            }

            if (length >= 3 && !IsValidContinuationRange(lead, bytes[index + 1]))
            {
                return false;
            }

            for (var offset = 1; offset < length; offset++)
            {
                if ((bytes[index + offset] & 0xC0) != 0x80)
                {
                    return false;
                }
            }

            index += length;
        }

        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Length in bytes of the UTF-8 sequence that starts with <paramref name="lead"/>, or 0 when the
    /// lead byte can never start a sequence.
    /// </summary>
    private static int SequenceLength(byte lead) => lead switch
    {
        < 0x80 => 1,
        >= 0xC2 and <= 0xDF => 2,
        >= 0xE0 and <= 0xEF => 3,
        >= 0xF0 and <= 0xF4 => 4,
        _ => 0,
    };

    private static bool IsValidContinuationRange(byte lead, byte second)
    {
        var secondValue = second & 0x3F;
        return lead switch
        {
            0xE0 => secondValue >= 0x20,
            0xED => secondValue <= 0x1F,
            0xF0 => secondValue >= 0x10,
            0xF4 => secondValue <= 0x0F,
            _ => true,
        };
    }

    /// <summary>
    /// Walks every object in the document and refuses a repeated member name at any depth.
    /// </summary>
    private static bool HasUniqueKeys(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in element.EnumerateObject())
                {
                    // Well-formedness was already decided before this walk, so every name materializes.
                    if (!names.Add(member.Name) || !HasUniqueKeys(member.Value))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (!HasUniqueKeys(item))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return true;
        }
    }

    /// <summary>
    /// Walks the raw document and inspects every <c>\uXXXX</c> escape in it, property names and string
    /// values alike, so an escape that spells half of a surrogate pair is refused here. Such a manifest is
    /// legal JSON and parses, but materializing any of its strings throws, so the refusal has to be decided
    /// from the raw text: the value cannot be read in order to be checked. The check reads the escape only,
    /// and nothing it inspects is echoed into a report.
    /// </summary>
    private static bool HasWellFormedStringTokens(string text)
    {
        var raw = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(
            raw,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumNestingDepth,
            });

        // The token walk is only a guard: a reader that cannot reach a string token at all still leaves the
        // byte-level scan below as the authority, so a throw here never becomes an unhandled exception.
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.String
                    && !HasWellFormedEscapes(reader.ValueSpan))
                {
                    return false;
                }
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        // The walk does not cover a property name whose escape cannot be decoded, because that name is not
        // exposed as an inspectable token. The raw bytes of the whole document are therefore scanned too,
        // and a lone half anywhere in a string position is the verdict.
        return HasWellFormedEscapes(raw);
    }

    /// <summary>
    /// True when every <c>\uXXXX</c> escape in one raw JSON token is a whole surrogate pair or no surrogate
    /// at all. The scan is exact: an even run of backslashes before <c>u</c> means the escape is escaped
    /// itself and carries no character, and a high surrogate is accepted only when the very next escape is
    /// its low half.
    /// </summary>
    private static bool HasWellFormedEscapes(ReadOnlySpan<byte> raw)
    {
        var index = 0;
        while (index < raw.Length)
        {
            if (raw[index] != (byte)'\\')
            {
                index++;
                continue;
            }

            var slashes = 0;
            while (index + slashes < raw.Length && raw[index + slashes] == (byte)'\\')
            {
                slashes++;
            }

            var marker = index + slashes;
            if (slashes % 2 == 1 && marker < raw.Length && raw[marker] == (byte)'u')
            {
                if (!TryReadEscape(raw, marker + 1, out var code))
                {
                    return false;
                }

                if (char.IsHighSurrogate((char)code))
                {
                    // The low half must follow immediately as the next escape; nothing may sit between.
                    if (!TryReadEscape(raw, marker + 6, out var low)
                        || !char.IsLowSurrogate((char)low))
                    {
                        return false;
                    }

                    index = marker + 11;
                    continue;
                }

                if (char.IsLowSurrogate((char)code))
                {
                    return false;
                }

                index = marker + 5;
                continue;
            }

            index = marker;
        }

        return true;
    }

    /// <summary>
    /// Reads the four hexadecimal digits of one <c>\uXXXX</c> escape that starts at <paramref name="start"/>.
    /// </summary>
    private static bool TryReadEscape(ReadOnlySpan<byte> raw, int start, out int code)
    {
        code = 0;
        if (start + 4 > raw.Length)
        {
            return false;
        }

        for (var offset = 0; offset < 4; offset++)
        {
            var digit = HexValue((char)raw[start + offset]);
            if (digit < 0)
            {
                return false;
            }

            code = (code << 4) | digit;
        }

        return true;
    }

    private static int HexValue(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => -1,
    };

    /// <summary>
    /// Walks the document and refuses any numeric member named in <see cref="IntegerFields"/> whose raw
    /// token is not a plain decimal integer.
    /// </summary>
    private static bool HasIntegerLexemesOnly(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    if (Array.IndexOf(IntegerFields, member.Name) >= 0
                        && member.Value.ValueKind == JsonValueKind.Number
                        && !IsIntegerLexeme(member.Value.GetRawText()))
                    {
                        return false;
                    }

                    if (!HasIntegerLexemesOnly(member.Value))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (!HasIntegerLexemesOnly(item))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return true;
        }
    }

    /// <summary>
    /// True only for an optional minus sign followed by digits with no leading zero.
    /// </summary>
    private static bool IsIntegerLexeme(string raw)
    {
        var digits = raw.StartsWith('-') ? raw.AsSpan(1) : raw.AsSpan();
        if (digits.Length == 0)
        {
            return false;
        }

        if (digits.Length > 1 && digits[0] == '0')
        {
            return false;
        }

        foreach (var character in digits)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
