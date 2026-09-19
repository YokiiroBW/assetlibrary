using System.Globalization;
using System.Text.Json;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Domain;

/// <summary>
/// Pure JSON shape, type and identity reading of one manifest document. It owns the exact-key sets of
/// the root object, of every <c>selected_parts</c> entry and of every <c>files</c> entry, the nullable
/// versus missing distinction, the raw number lexemes and the identity vocabularies. It reads no file,
/// no directory, no clock and no environment, and it never inspects the package layout.
/// </summary>
/// <remarks>
/// Every diagnostic it records uses a field position such as <c>files[2].size_bytes</c> rather than a
/// manifest-supplied string, so an unverified path can never reach a report through this layer.
/// </remarks>
public static class MediaPackageShapePolicy
{
    public const int SupportedSchemaVersion = 1;
    public const string RequiredProvider = "bilibili";

    public const int MinimumFiles = 3;
    public const int MaximumFiles = 512;
    public const int MaximumSelectedParts = 128;
    public const int MaximumEpisodeNumber = 999999;

    public const long MaximumFileSizeBytes = 137_438_953_472;
    public const long MaximumNfoOrSourceBytes = 4_194_304;
    public const long MaximumImageBytes = 33_554_432;

    public const int MaximumStagingRefLength = 64;

    private static readonly string[] RootKeys =
    [
        "schema_version",
        "package_id",
        "staging_ref",
        "library_id",
        "provider",
        "bvid",
        "layout",
        "media_extension",
        "selected_parts",
        "files",
    ];

    private static readonly string[] SelectedPartKeys = ["cid", "episode_number"];

    private static readonly string[] FileKeys = ["path", "kind", "cid", "size_bytes", "sha256"];

    /// <summary>
    /// Reads the whole manifest shape. Returns null when the document is unacceptable for any reason;
    /// the reason is always named in <paramref name="issues"/>.
    /// </summary>
    public static MediaPackageShape? Read(JsonElement root, MediaPackageIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (!TryObject(root, "manifest", RootKeys, issues, out var fields))
        {
            return null;
        }

        if (!TryVersion(Field(fields, "schema_version"), issues, out var version)
            || version != SupportedSchemaVersion)
        {
            return null;
        }

        if (!TryUuid(Field(fields, "package_id"), "package_id", issues, out var packageId)
            || !TryUuid(Field(fields, "library_id"), "library_id", issues, out var libraryId)
            || !TryStagingRef(Field(fields, "staging_ref"), issues, out var stagingRef)
            || !TryString(Field(fields, "provider"), "provider", issues, out var provider)
            || !TryString(Field(fields, "bvid"), "bvid", issues, out var bvid)
            || !TryString(Field(fields, "media_extension"), "media_extension", issues, out var extension)
            || !TryLayout(Field(fields, "layout"), issues, out var layout)
            || !TrySelectedParts(Field(fields, "selected_parts"), issues, out var parts)
            || !TryFiles(Field(fields, "files"), issues, out var files))
        {
            return null;
        }

        if (!string.Equals(provider, RequiredProvider, StringComparison.Ordinal))
        {
            issues.Record("invalid_identity", "provider");
        }

        if (!IsBvid(bvid))
        {
            issues.Record("invalid_identity", "bvid");
        }

        if (extension is not ("mp4" or "mkv"))
        {
            issues.Record("invalid_layout", "media_extension");
        }

        if (issues.HasIssues)
        {
            return null;
        }

        return new MediaPackageShape(
            packageId.ToString("D", CultureInfo.InvariantCulture),
            stagingRef,
            new LibraryId(libraryId),
            bvid,
            layout,
            extension,
            parts,
            files);
    }

    /// <summary>
    /// True when the object carries exactly the expected member names: a missing required key and an
    /// unknown key are both refusals, at every nesting level.
    /// </summary>
    public static bool HasExactKeys(JsonElement value, IReadOnlyList<string> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var seen = 0;
        foreach (var member in value.EnumerateObject())
        {
            if (!expected.Contains(member.Name, StringComparer.Ordinal))
            {
                return false;
            }

            seen++;
        }

        return seen == expected.Count;
    }

    /// <summary>
    /// True for a JSON string that is a canonical lowercase <c>D</c> UUID and not the all-zero value.
    /// </summary>
    public static bool IsCanonicalUuid(string value) =>
        Guid.TryParseExact(value, "D", out var parsed)
        && parsed != Guid.Empty
        && string.Equals(value, parsed.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>
    /// True for <c>BV</c> followed by ten ASCII letters or digits.
    /// </summary>
    public static bool IsBvid(string value) =>
        value.Length == 12
        && value.StartsWith("BV", StringComparison.Ordinal)
        && value.AsSpan(2).ToArray().All(char.IsAsciiLetterOrDigit);

    /// <summary>
    /// True for a lowercase 64-digit hexadecimal digest.
    /// </summary>
    public static bool IsLowercaseSha256(string value) =>
        value.Length == 64
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    /// <summary>
    /// Accepts only JSON integer tokens. The reader already refuses fractional, exponent, leading-zero
    /// and quoted number lexemes, so this is the second half of the same rule: a value that is not an
    /// integer in range is never coerced.
    /// </summary>
    public static bool TryInteger(JsonElement value, out long number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number))
        {
            return true;
        }

        number = 0;
        return false;
    }

    /// <summary>
    /// Returns the named member, or an undefined element when the member is absent. An undefined
    /// element is never a valid value for any field, so absence is reported by the field check itself.
    /// </summary>
    public static JsonElement Field(JsonElement fields, string name) =>
        fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(name, out var member)
            ? member
            : default;

    private static bool TryVersion(JsonElement value, MediaPackageIssueSink issues, out int version)
    {
        if (!TryInteger(value, out var raw) || raw is < 0 or > int.MaxValue)
        {
            issues.Record("invalid_manifest", "schema_version");
            version = 0;
            return false;
        }

        version = (int)raw;
        if (version != SupportedSchemaVersion)
        {
            issues.Record(
                version > SupportedSchemaVersion ? "unsupported_version" : "invalid_manifest",
                "schema_version");
            return false;
        }

        return true;
    }

    private static bool TryObject(
        JsonElement value,
        string location,
        IReadOnlyList<string> expectedKeys,
        MediaPackageIssueSink issues,
        out JsonElement fields)
    {
        fields = value;
        if (value.ValueKind != JsonValueKind.Object)
        {
            issues.Record("invalid_manifest", location);
            return false;
        }

        if (!HasExactKeys(value, expectedKeys))
        {
            issues.Record("invalid_manifest", location);
            return false;
        }

        return true;
    }

    private static bool TryUuid(
        JsonElement value,
        string field,
        MediaPackageIssueSink issues,
        out Guid parsed)
    {
        if (value.ValueKind != JsonValueKind.String || !IsCanonicalUuid(value.GetString()!))
        {
            issues.Record("invalid_identity", field);
            parsed = Guid.Empty;
            return false;
        }

        parsed = Guid.ParseExact(value.GetString()!, "D");
        return true;
    }

    private static bool TryStagingRef(
        JsonElement value,
        MediaPackageIssueSink issues,
        out string text)
    {
        text = value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
        var invalid = value.ValueKind != JsonValueKind.String
            || text.Length is 0 or > MaximumStagingRefLength
            || !char.IsAsciiLetterOrDigit(text[0])
            || text.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_');
        if (invalid)
        {
            issues.Record("invalid_identity", "staging_ref");
            return false;
        }

        return true;
    }

    private static bool TryString(
        JsonElement value,
        string field,
        MediaPackageIssueSink issues,
        out string text)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            issues.Record("invalid_manifest", field);
            text = string.Empty;
            return false;
        }

        text = value.GetString()!;
        return true;
    }

    private static bool TryLayout(
        JsonElement value,
        MediaPackageIssueSink issues,
        out MediaPackageLayout layout)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            switch (value.GetString())
            {
                case "single":
                    layout = MediaPackageLayout.SinglePart;
                    return true;
                case "multipart":
                    layout = MediaPackageLayout.Multipart;
                    return true;
                default:
                    break;
            }
        }

        issues.Record("invalid_layout", "layout");
        layout = MediaPackageLayout.SinglePart;
        return false;
    }

    private static bool TrySelectedParts(
        JsonElement value,
        MediaPackageIssueSink issues,
        out IReadOnlyList<MediaPackageSelectedPart> parts)
    {
        parts = [];
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is < 1 or > MaximumSelectedParts)
        {
            issues.Record("invalid_manifest", "selected_parts");
            return false;
        }

        var read = new List<MediaPackageSelectedPart>(value.GetArrayLength());
        var cids = new HashSet<string>(StringComparer.Ordinal);
        var episodes = new HashSet<int>();
        var index = -1;
        foreach (var element in value.EnumerateArray())
        {
            index++;
            var location = $"selected_parts[{index}]";
            if (!TryObject(element, location, SelectedPartKeys, issues, out var fields)
                || !TryCid(Field(fields, "cid"), location + ".cid", issues, out var cid))
            {
                return false;
            }

            if (!cids.Add(cid))
            {
                issues.Record("invalid_identity", location + ".cid");
                return false;
            }

            if (!TryEpisodeNumber(Field(fields, "episode_number"), location, issues, out var episode))
            {
                return false;
            }

            if (episode is not null && !episodes.Add(episode.Value))
            {
                issues.Record("invalid_layout", location + ".episode_number");
                return false;
            }

            read.Add(new MediaPackageSelectedPart(cid, episode));
        }

        parts = read;
        return true;
    }

    private static bool TryFiles(
        JsonElement value,
        MediaPackageIssueSink issues,
        out IReadOnlyList<MediaPackageFileEntry> files)
    {
        files = [];
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is < MinimumFiles or > MaximumFiles)
        {
            issues.Record("invalid_manifest", "files");
            return false;
        }

        var read = new List<MediaPackageFileEntry>(value.GetArrayLength());
        var index = -1;
        foreach (var element in value.EnumerateArray())
        {
            index++;
            var location = $"files[{index}]";
            if (!TryObject(element, location, FileKeys, issues, out var fields)
                || !TryString(Field(fields, "path"), location + ".path", issues, out var path)
                || !TryKind(Field(fields, "kind"), location + ".kind", issues, out var kind)
                || !TrySha256(Field(fields, "sha256"), location + ".sha256", issues, out var sha256)
                || !TrySize(Field(fields, "size_bytes"), location + ".size_bytes", issues, out var size)
                || !TryNullableCid(Field(fields, "cid"), location + ".cid", issues, out var cid))
            {
                return false;
            }

            read.Add(new MediaPackageFileEntry(path, kind, cid, size, new Sha256Digest(sha256)));
        }

        files = read;
        return true;
    }

    /// <summary>
    /// A <c>files[].cid</c> is either a valid cid or an explicit JSON null. A missing member was already
    /// refused by the exact-key check, so null and absent are never conflated.
    /// </summary>
    private static bool TryNullableCid(
        JsonElement value,
        string location,
        MediaPackageIssueSink issues,
        out string? cid)
    {
        cid = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        return TryCid(value, location, issues, out cid!);
    }

    private static bool TryCid(
        JsonElement value,
        string location,
        MediaPackageIssueSink issues,
        out string cid)
    {
        cid = string.Empty;
        if (value.ValueKind != JsonValueKind.String)
        {
            issues.Record("invalid_identity", location);
            return false;
        }

        var text = value.GetString()!;
        var invalid = text.Length is < 1 or > 20
            || text[0] == '0'
            || !text.All(char.IsAsciiDigit);
        if (invalid)
        {
            issues.Record("invalid_identity", location);
            return false;
        }

        cid = text;
        return true;
    }

    private static bool TryEpisodeNumber(
        JsonElement value,
        string location,
        MediaPackageIssueSink issues,
        out int? episode)
    {
        episode = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (!TryInteger(value, out var number) || number is < 1 or > MaximumEpisodeNumber)
        {
            issues.Record("invalid_layout", location + ".episode_number");
            return false;
        }

        episode = (int)number;
        return true;
    }

    private static bool TrySize(
        JsonElement value,
        string location,
        MediaPackageIssueSink issues,
        out long size)
    {
        if (!TryInteger(value, out size) || size is < 1 or > MaximumFileSizeBytes)
        {
            issues.Record("invalid_manifest", location);
            size = 0;
            return false;
        }

        return true;
    }

    private static bool TrySha256(
        JsonElement value,
        string location,
        MediaPackageIssueSink issues,
        out string sha256)
    {
        if (value.ValueKind != JsonValueKind.String || !IsLowercaseSha256(value.GetString()!))
        {
            issues.Record("invalid_manifest", location);
            sha256 = string.Empty;
            return false;
        }

        sha256 = value.GetString()!;
        return true;
    }

    private static bool TryKind(
        JsonElement value,
        string location,
        MediaPackageIssueSink issues,
        out MediaPackageFileKind kind)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            switch (value.GetString())
            {
                case "video":
                    kind = MediaPackageFileKind.Video;
                    return true;
                case "nfo":
                    kind = MediaPackageFileKind.Nfo;
                    return true;
                case "source":
                    kind = MediaPackageFileKind.Source;
                    return true;
                case "poster":
                    kind = MediaPackageFileKind.Poster;
                    return true;
                case "episode_thumb":
                    kind = MediaPackageFileKind.EpisodeThumb;
                    return true;
                default:
                    break;
            }
        }

        issues.Record("invalid_file_set", location);
        kind = MediaPackageFileKind.Nfo;
        return false;
    }
}

/// <summary>
/// The shape-validated content of one manifest, before any layout or file-set rule is applied. The
/// collections are owned by this instance and never aliased to the caller's arrays.
/// </summary>
public sealed record MediaPackageShape(
    string PackageId,
    string StagingRef,
    LibraryId LibraryId,
    string Bvid,
    MediaPackageLayout Layout,
    string MediaExtension,
    IReadOnlyList<MediaPackageSelectedPart> SelectedParts,
    IReadOnlyList<MediaPackageFileEntry> Files);
