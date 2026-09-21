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
            // syntactically legal JSON, but materializing any string then throws, so the decode guard runs
            // before any member name or value is used, as a manifest verdict: the exception type and the
            // offending text never reach a report. Legal escape pairs and the same characters written as
            // UTF-8 pass here and are judged by field semantics instead.
            if (!TryDecodeEveryString(manifestBytes))
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
    /// Forces the JSON reader to decode every property name and every string value in the document, so an
    /// input whose decoding fails is refused as a manifest verdict instead of escaping as a framework
    /// exception later. The check uses the reader's own token and decoding rules rather than a hand-written
    /// escape scan: the same character written as UTF-8 and written as a legal escape pair therefore behave
    /// identically here, a literal <c>\\uD800</c> (an escaped backslash followed by the letter u) is an
    /// ordinary string, and a lone surrogate half is a decode failure. It is only a syntax guard: a
    /// well-formed character that a field does not allow is refused later by the identity and layout
    /// validation, which is where field semantics belong. Nothing inspected here is echoed into a report.
    /// </summary>
    private static bool TryDecodeEveryString(byte[] raw)
    {
        var reader = new Utf8JsonReader(
            raw,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumNestingDepth,
            });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String)
                {
                    // Materializing is what decodes: a lone surrogate half throws here, and nowhere else.
                    _ = reader.GetString();
                }
            }
        }
        catch (InvalidOperationException)
        {
            // A string whose escapes do not decode to well-formed text. The exception type and the
            // offending text are both discarded; the caller records the named verdict.
            return false;
        }
        catch (JsonException)
        {
            // The document already parsed, so this cannot be a structural error: the reader is reporting a
            // token it refuses, which is the same input problem and gets the same named verdict rather than
            // escaping as a framework exception.
            return false;
        }

        return true;
    }

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
