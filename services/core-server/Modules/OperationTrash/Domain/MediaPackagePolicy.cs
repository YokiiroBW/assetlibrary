using System.Globalization;
using System.Text.Json;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Domain;

/// <summary>
/// Pure structural policy of a prepared inbound media package: identity, layout, declared file set and
/// declared budgets. It reads no file, no directory, no clock and no environment; every decision is a
/// function of the manifest text alone.
/// </summary>
public static class MediaPackagePolicy
{
    public const int SupportedSchemaVersion = 1;
    public const string RequiredProvider = "bilibili";

    public const int MinimumFiles = 3;
    public const int MaximumFiles = 512;
    public const int MaximumSelectedParts = 128;
    public const int MaximumEpisodeNumber = 999999;

    public const long MaximumFileSizeBytes = 137_438_953_472;
    public const long MaximumDeclaredTotalBytes = 1_099_511_627_776;
    public const long MaximumNfoOrSourceBytes = 4_194_304;
    public const long MaximumImageBytes = 33_554_432;

    private static readonly string[] RequiredFields =
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

    /// <summary>
    /// Validates the whole manifest shape and cross-field policy. <paramref name="root"/> is the parsed
    /// strict JSON document root of the received bytes. Returns null when the manifest is unacceptable;
    /// only a non-null result may continue to authorization.
    /// </summary>
    public static MediaPackageManifest? Validate(JsonElement root, MediaPackageIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (!TryObject(root, "manifest", issues, out var fields))
        {
            return null;
        }

        if (!HasRequiredFields(fields, issues))
        {
            return null;
        }

        ValidateVersion(Field(fields, "schema_version"), issues);
        _ = TryUuid(Field(fields, "package_id"), "package_id", issues);
        ValidateStagingRef(Field(fields, "staging_ref"), issues);
        var libraryUuid = TryUuid(Field(fields, "library_id"), "library_id", issues);
        if (!StringField(fields, "package_id", issues, out var packageId)
            || !StringField(fields, "staging_ref", issues, out var stagingRef)
            || !StringField(fields, "provider", issues, out var provider)
            || !StringField(fields, "bvid", issues, out var bvid)
            || !StringField(fields, "media_extension", issues, out var mediaExtension)
            || libraryUuid is null
            || !TryLayout(Field(fields, "layout"), issues, out var layout))
        {
            return null;
        }

        if (provider != RequiredProvider)
        {
            issues.Record("invalid_identity", "provider");
        }

        if (!IsBvid(bvid))
        {
            issues.Record("invalid_identity", "bvid");
        }

        if (mediaExtension is not ("mp4" or "mkv"))
        {
            issues.Record("invalid_layout", "media_extension");
        }

        var partsReadable = TrySelectedParts(Field(fields, "selected_parts"), issues, out var parts);
        var filesReadable = TryFiles(Field(fields, "files"), issues, out var files);
        ValidateSelectedPartsAgainstLayout(layout, parts, issues);
        ValidateFileShape(files, issues);
        ValidateDeclaredBudgets(files, issues);
        if (partsReadable && filesReadable)
        {
            ValidateCrossFields(layout, parts, files, issues);
        }

        if (issues.HasIssues || !partsReadable || !filesReadable)
        {
            return null;
        }

        return new MediaPackageManifest(
            packageId,
            stagingRef,
            new LibraryId(libraryUuid.Value),
            bvid,
            layout,
            mediaExtension,
            parts,
            files);
    }

    /// <summary>
    /// Aligns the declared file set with the real directory listing. <paramref name="actualFiles"/> and
    /// <paramref name="actualDirectories"/> hold every file and directory found under the package root
    /// as package-relative POSIX paths.
    /// </summary>
    public static bool ValidateFileSet(
        MediaPackageManifest manifest,
        IReadOnlyList<string> actualFiles,
        IReadOnlyList<string> actualDirectories,
        MediaPackageIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(actualFiles);
        ArgumentNullException.ThrowIfNull(actualDirectories);
        ArgumentNullException.ThrowIfNull(issues);

        var declared = new HashSet<string>(
            manifest.Files.Select(file => file.Path),
            StringComparer.Ordinal);
        var observed = new HashSet<string>(actualFiles, StringComparer.Ordinal);
        foreach (var path in actualFiles.Where(path => !declared.Contains(path)))
        {
            issues.Record("invalid_file_set", path);
        }

        foreach (var path in declared.Where(path => !observed.Contains(path)))
        {
            issues.Record("source_missing", path);
        }

        var required = RequiredDirectories(manifest);
        foreach (var directory in actualDirectories.Where(directory => !required.Contains(directory)))
        {
            issues.Record("invalid_file_set", directory + "/");
        }

        return issues.IsEmpty;
    }

    /// <summary>
    /// Directories the fixed layout implies. Every other directory under the package root, including an
    /// empty one, is an undeclared object.
    /// </summary>
    public static HashSet<string> RequiredDirectories(MediaPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            var separator = file.Path.IndexOf('/');
            if (separator > 0)
            {
                required.Add(file.Path[..separator]);
            }
        }

        return required;
    }

    /// <summary>
    /// Target package directory name inside the trusted library root. The client never chooses it.
    /// </summary>
    public static string TargetDirectoryName(MediaPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Layout == MediaPackageLayout.SinglePart
            ? $"bilibili-{manifest.Bvid}-cid-{manifest.SelectedParts[0].Cid}"
            : $"bilibili-{manifest.Bvid}";
    }

    private static bool HasRequiredFields(JsonElement fields, MediaPackageIssueSink issues)
    {
        var complete = true;
        foreach (var name in RequiredFields)
        {
            if (!fields.TryGetProperty(name, out _))
            {
                issues.Record("invalid_manifest", name);
                complete = false;
            }
        }

        return complete;
    }

    private static void ValidateVersion(JsonElement value, MediaPackageIssueSink issues)
    {
        if (!TryInteger(value, out var version))
        {
            issues.Record("invalid_manifest", "schema_version");
            return;
        }

        if (version != SupportedSchemaVersion)
        {
            issues.Record(
                version > SupportedSchemaVersion ? "unsupported_version" : "invalid_manifest",
                "schema_version");
        }
    }

    private static Guid? TryUuid(JsonElement value, string field, MediaPackageIssueSink issues)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            issues.Record("invalid_identity", field);
            return null;
        }

        var text = value.GetString()!;
        if (!Guid.TryParseExact(text, "D", out var parsed)
            || parsed == Guid.Empty
            || !string.Equals(text, parsed.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            issues.Record("invalid_identity", field);
            return null;
        }

        return parsed;
    }

    private static void ValidateStagingRef(JsonElement value, MediaPackageIssueSink issues)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            issues.Record("invalid_identity", "staging_ref");
            return;
        }

        var text = value.GetString()!;
        var invalid = text.Length is 0 or > 64
            || !char.IsAsciiLetterOrDigit(text[0])
            || text.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_');
        if (invalid)
        {
            issues.Record("invalid_identity", "staging_ref");
        }
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
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is < 1 or > MaximumSelectedParts)
        {
            issues.Record("invalid_manifest", "selected_parts");
            parts = [];
            return false;
        }

        var elements = value.EnumerateArray().ToArray();
        var read = new List<MediaPackageSelectedPart>(elements.Length);
        var cids = new HashSet<string>(StringComparer.Ordinal);
        var episodes = new HashSet<int>();
        var valid = true;
        for (var index = 0; index < elements.Length; index++)
        {
            var location = $"selected_parts[{index}]";
            if (!TryObject(elements[index], location, issues, out var fields))
            {
                valid = false;
                continue;
            }

            var cid = TryCid(Field(fields, "cid"), location + ".cid", issues);
            if (cid is null)
            {
                valid = false;
            }
            else if (!cids.Add(cid))
            {
                issues.Record("invalid_identity", location + ".cid");
            }

            var hasEpisode = fields.TryGetProperty("episode_number", out var episodeValue);
            var episode = hasEpisode
                ? TryEpisodeNumber(episodeValue, location, issues)
                : null;
            if (hasEpisode
                && episodeValue.ValueKind != JsonValueKind.Null
                && episode is null)
            {
                valid = false;
            }

            if (episode is not null && !episodes.Add(episode.Value))
            {
                issues.Record("invalid_layout", location + ".episode_number");
            }

            if (cid is not null)
            {
                read.Add(new MediaPackageSelectedPart(cid, episode));
            }
        }

        parts = read;
        return valid;
    }

    private static string? TryCid(JsonElement value, string location, MediaPackageIssueSink issues)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            issues.Record("invalid_identity", location);
            return null;
        }

        var text = value.GetString()!;
        var invalid = text.Length is < 1 or > 20
            || text[0] == '0'
            || !text.All(char.IsAsciiDigit);
        if (invalid)
        {
            issues.Record("invalid_identity", location);
            return null;
        }

        return text;
    }

    private static int? TryEpisodeNumber(
        JsonElement value,
        string location,
        MediaPackageIssueSink issues)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!TryInteger(value, out var number) || number is < 1 or > MaximumEpisodeNumber)
        {
            issues.Record("invalid_layout", location + ".episode_number");
            return null;
        }

        return (int)number;
    }

    private static bool TryFiles(
        JsonElement value,
        MediaPackageIssueSink issues,
        out IReadOnlyList<MediaPackageFileEntry> files)
    {
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is < MinimumFiles or > MaximumFiles)
        {
            issues.Record("invalid_manifest", "files");
            files = [];
            return false;
        }

        var elements = value.EnumerateArray().ToArray();
        var read = new List<MediaPackageFileEntry>(elements.Length);
        var valid = true;
        for (var index = 0; index < elements.Length; index++)
        {
            var location = $"files[{index}]";
            if (!TryObject(elements[index], location, issues, out var fields))
            {
                valid = false;
                continue;
            }

            if (!StringField(fields, "path", issues, out var path)
                || !TryKind(Field(fields, "kind"), location + ".kind", issues, out var kind)
                || !StringField(fields, "sha256", issues, out var sha256))
            {
                valid = false;
                continue;
            }

            if (!IsLowercaseSha256(sha256))
            {
                issues.Record("invalid_manifest", location + ".sha256");
                valid = false;
                continue;
            }

            var hasCid = fields.TryGetProperty("cid", out var cidValue);
            var cid = hasCid ? TryCid(cidValue, location + ".cid", issues) : null;
            if (hasCid && cidValue.ValueKind != JsonValueKind.Null && cid is null)
            {
                valid = false;
            }

            if (!TryInteger(Field(fields, "size_bytes"), out var size)
                || size is < 1 or > MaximumFileSizeBytes)
            {
                issues.Record("invalid_manifest", location + ".size_bytes");
                valid = false;
                continue;
            }

            read.Add(new MediaPackageFileEntry(path, kind, cid, size, new Sha256Digest(sha256)));
        }

        files = read;
        return valid;
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

    /// <summary>
    /// Per-file obligations that depend on the kind: raw path spelling, layout template, per-kind size
    /// ceiling and case-folded collisions between declared paths.
    /// </summary>
    private static void ValidateFileShape(
        IReadOnlyList<MediaPackageFileEntry> files,
        MediaPackageIssueSink issues)
    {
        foreach (var file in files)
        {
            var pathFailure = MediaPackagePathPolicy.Validate(file.Path);
            if (pathFailure is not null)
            {
                issues.Record(pathFailure, file.Path);
                continue;
            }

            if (!MatchesLayout(file, issues))
            {
                continue;
            }

            if (file.Kind is MediaPackageFileKind.Video)
            {
                continue;
            }

            var ceiling = file.Kind is MediaPackageFileKind.Nfo or MediaPackageFileKind.Source
                ? MaximumNfoOrSourceBytes
                : MaximumImageBytes;
            if (file.SizeBytes > ceiling)
            {
                issues.Record("invalid_file_set", file.Path);
            }
        }

        for (var left = 0; left < files.Count; left++)
        {
            for (var right = left + 1; right < files.Count; right++)
            {
                if (MediaPackagePathPolicy.Collides(files[left].Path, files[right].Path))
                {
                    issues.Record("duplicate_path", files[right].Path);
                }
            }
        }
    }

    private static bool MatchesLayout(MediaPackageFileEntry file, MediaPackageIssueSink issues)
    {
        var segments = file.Path.Split('/');
        var extension = Extension(segments[^1]);
        var rootScoped = segments.Length == 1;
        var isEpisodeEntry = file.Kind is MediaPackageFileKind.Video
            or MediaPackageFileKind.Nfo
            or MediaPackageFileKind.EpisodeThumb;

        // Exactly two shapes exist: a file directly in the package root, or one episode entry inside
        // the single "Season NN" directory.
        if (rootScoped && file.Kind is not (MediaPackageFileKind.Nfo
            or MediaPackageFileKind.Source
            or MediaPackageFileKind.Poster
            or MediaPackageFileKind.Video))
        {
            issues.Record("invalid_file_set", file.Path);
            return false;
        }

        if (!rootScoped
            && (!segments[0].StartsWith("Season ", StringComparison.Ordinal) || !isEpisodeEntry))
        {
            issues.Record("invalid_file_set", file.Path);
            return false;
        }

        if (extension.Length == 0)
        {
            issues.Record("invalid_path", file.Path);
            return false;
        }

        if (!isEpisodeEntry)
        {
            return true;
        }

        // A cid-scoped file must name its own cid, and every non-cid file must have none.
        var namesCid = file.Cid is not null
            && file.Path.Contains("-cid-" + file.Cid, StringComparison.Ordinal);
        if (file.Cid is null || !namesCid)
        {
            issues.Record("invalid_file_set", file.Path);
            return false;
        }

        return true;
    }

    private static string Extension(string name)
    {
        var separator = name.LastIndexOf('.');
        return separator <= 0 ? string.Empty : name[(separator + 1)..];
    }

    private static void ValidateSelectedPartsAgainstLayout(
        MediaPackageLayout layout,
        IReadOnlyList<MediaPackageSelectedPart> parts,
        MediaPackageIssueSink issues)
    {
        if (layout == MediaPackageLayout.SinglePart)
        {
            if (parts.Count != 1)
            {
                issues.Record("invalid_layout", "selected_parts");
                return;
            }

            if (parts[0].EpisodeNumber is not null)
            {
                issues.Record("invalid_layout", "selected_parts[0].episode_number");
            }

            return;
        }

        if (parts.Any(part => part.EpisodeNumber is null))
        {
            issues.Record("invalid_layout", "selected_parts");
        }
    }

    /// <summary>
    /// Declared budgets. The service read budget is smaller and cannot be raised by the manifest, so
    /// these are only the upper bounds the contract itself admits.
    /// </summary>
    private static void ValidateDeclaredBudgets(
        IReadOnlyList<MediaPackageFileEntry> files,
        MediaPackageIssueSink issues)
    {
        if (files.Count > MaximumFiles)
        {
            return;
        }

        long total = 0;
        foreach (var file in files)
        {
            total = Math.Max(
                total,
                file.SizeBytes > long.MaxValue - total ? long.MaxValue : total + file.SizeBytes);
        }

        if (total > MaximumDeclaredTotalBytes)
        {
            issues.Record("budget_exceeded", "files");
        }
    }

    /// <summary>
    /// Cross-field obligations between declared files and selected parts: exactly one video and one nfo
    /// per selected cid, exactly one source.json, the layout-specific root nfo and the optional
    /// poster/thumbnail cardinality.
    /// </summary>
    private static void ValidateCrossFields(
        MediaPackageLayout layout,
        IReadOnlyList<MediaPackageSelectedPart> parts,
        IReadOnlyList<MediaPackageFileEntry> files,
        MediaPackageIssueSink issues)
    {
        var selectedCids = new HashSet<string>(parts.Select(part => part.Cid), StringComparer.Ordinal);
        foreach (var file in files)
        {
            var cidMismatch = file.Kind switch
            {
                MediaPackageFileKind.Source or MediaPackageFileKind.Poster => file.Cid is not null,
                MediaPackageFileKind.Video => file.Cid is null,
                _ => false,
            };
            if (cidMismatch)
            {
                issues.Record("invalid_file_set", file.Path);
                continue;
            }

            if (file.Cid is not null && !selectedCids.Contains(file.Cid))
            {
                issues.Record("invalid_file_set", file.Path);
            }
        }

        if (files.Count(file => file.Kind == MediaPackageFileKind.Source) != 1)
        {
            issues.Record("invalid_file_set", "source.json");
        }

        if (files.Count(file => file.Kind == MediaPackageFileKind.Poster) > 1)
        {
            issues.Record("invalid_file_set", "poster");
        }

        foreach (var cid in selectedCids)
        {
            var hasOneVideo = files.Count(file =>
                file.Kind == MediaPackageFileKind.Video && file.Cid == cid) == 1;
            var hasOneNfo = files.Count(file =>
                file.Kind == MediaPackageFileKind.Nfo && file.Cid == cid) == 1;
            var hasOneThumb = files.Count(file =>
                file.Kind == MediaPackageFileKind.EpisodeThumb && file.Cid == cid) <= 1;
            if (!hasOneVideo || !hasOneNfo || !hasOneThumb)
            {
                issues.Record("invalid_file_set", "cid:" + cid);
            }
        }

        var layoutNfo = layout == MediaPackageLayout.SinglePart ? "movie.nfo" : "tvshow.nfo";
        var hasLayoutNfo = files.Count(file =>
            file.Kind == MediaPackageFileKind.Nfo
            && file.Cid is null
            && string.Equals(file.Path, layoutNfo, StringComparison.Ordinal)) == 1;
        if (!hasLayoutNfo)
        {
            issues.Record("invalid_file_set", layoutNfo);
        }
    }

    private static bool TryObject(
        JsonElement value,
        string location,
        MediaPackageIssueSink issues,
        out JsonElement fields)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            fields = value;
            return true;
        }

        issues.Record("invalid_manifest", location);
        fields = default;
        return false;
    }

    /// <summary>
    /// Returns the named member, or an undefined element when the member is absent. An undefined
    /// element is never a valid value for any field, so absence is reported by the field check itself.
    /// </summary>
    private static JsonElement Field(JsonElement fields, string name) =>
        fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(name, out var member)
            ? member
            : default;

    private static bool StringField(
        JsonElement fields,
        string name,
        MediaPackageIssueSink issues,
        out string value)
    {
        if (fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty(name, out var raw)
            && raw.ValueKind == JsonValueKind.String)
        {
            value = raw.GetString()!;
            return true;
        }

        issues.Record("invalid_manifest", name);
        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Accepts only JSON integer tokens. The reader already refuses fractional, exponent, leading-zero
    /// and quoted number lexemes, so this is the second half of the same rule: a value that is not an
    /// integer in range is never coerced.
    /// </summary>
    private static bool TryInteger(JsonElement value, out long number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number))
        {
            return true;
        }

        number = 0;
        return false;
    }

    private static bool IsBvid(string value) =>
        value.Length == 12
        && value.StartsWith("BV", StringComparison.Ordinal)
        && value.AsSpan(2).ToArray().All(char.IsAsciiLetterOrDigit);

    private static bool IsLowercaseSha256(string value) =>
        value.Length == 64
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
}
