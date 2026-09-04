using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PwnedPasswordsRangeParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static bool TryParse(
        ReadOnlySpan<byte> body,
        int minimumLines,
        int maximumLines,
        out PwnedPasswordPrefixRange range)
    {
        range = null!;
        char[] characters;
        try
        {
            var characterCount = StrictUtf8.GetCharCount(body);
            characters = new char[characterCount];
            _ = StrictUtf8.GetChars(body, characters);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        try
        {
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            var remaining = characters.AsSpan();
            var lineCount = 0;
            while (!remaining.IsEmpty)
            {
                var lineFeed = remaining.IndexOf('\n');
                if (lineFeed <= 0 || remaining[lineFeed - 1] != '\r')
                {
                    return false;
                }

                var line = remaining[..(lineFeed - 1)];
                remaining = remaining[(lineFeed + 1)..];
                lineCount++;
                if (lineCount > maximumLines
                    || !TryParseLine(line, out var suffix, out var count))
                {
                    return false;
                }

                if (counts.ContainsKey(suffix))
                {
                    return false;
                }

                counts.Add(suffix, count);
            }

            if (lineCount < minimumLines)
            {
                return false;
            }

            var compromised = new List<string>(counts.Count);
            foreach (var pair in counts)
            {
                if (pair.Value > 0)
                {
                    compromised.Add(pair.Key);
                }
            }

            range = new PwnedPasswordPrefixRange(compromised.ToArray());
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
        }
    }

    private static bool TryParseLine(
        ReadOnlySpan<char> line,
        out string suffix,
        out long count)
    {
        suffix = string.Empty;
        count = 0;
        if (line.Length < PwnedPasswordQuery.SuffixCharacters + 2
            || line[PwnedPasswordQuery.SuffixCharacters] != ':'
            || !IsUpperHex(line[..PwnedPasswordQuery.SuffixCharacters])
            || !long.TryParse(
                line[(PwnedPasswordQuery.SuffixCharacters + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out count)
            || count < 0)
        {
            return false;
        }

        suffix = new string(line[..PwnedPasswordQuery.SuffixCharacters]);
        return true;
    }

    private static bool IsUpperHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character) && character is < 'A' or > 'F')
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class PwnedPasswordPrefixRange(string[] compromisedSuffixes)
{
    public bool IsCompromised(ReadOnlySpan<char> soughtSuffix)
    {
        var matched = false;
        foreach (var candidate in compromisedSuffixes)
        {
            var difference = candidate.Length ^ soughtSuffix.Length;
            var compared = Math.Min(candidate.Length, soughtSuffix.Length);
            for (var index = 0; index < compared; index++)
            {
                difference |= candidate[index] ^ soughtSuffix[index];
            }

            matched |= difference == 0;
        }

        return matched;
    }
}
