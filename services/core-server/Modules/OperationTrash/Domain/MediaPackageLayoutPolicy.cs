using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Domain;

/// <summary>
/// Pure layout policy: it turns <c>layout</c>, <c>media_extension</c> and <c>selected_parts</c> into the
/// exact required and optional file templates of the frozen candidate, and decides whether each declared
/// entry matches the template its <c>kind</c> claims, including the CID and episode-number mapping. It
/// decodes no JSON, reads no file and touches no clock or environment.
/// </summary>
public static class MediaPackageLayoutPolicy
{
    /// <summary>
    /// Which frozen template one declared entry was recognised as, or that it matched none.
    /// </summary>
    public enum EntryRole
    {
        Inapplicable = 0,
        SingleVideo = 1,
        SingleMovieNfo = 2,
        MultipartShowNfo = 3,
        MultipartVideo = 4,
        MultipartEpisodeNfo = 5,
        Source = 6,
        Poster = 7,
        EpisodeThumb = 8,
    }

    public static string SourcePath => "source.json";

    /// <summary>
    /// Validates the selected parts against the layout: single admits exactly one part whose episode
    /// number is null, multipart requires an episode number on every selected part.
    /// </summary>
    public static bool ValidateSelectedParts(
        MediaPackageLayout layout,
        IReadOnlyList<MediaPackageSelectedPart> parts,
        MediaPackageIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(issues);
        if (layout == MediaPackageLayout.SinglePart)
        {
            if (parts.Count != 1)
            {
                issues.Record("invalid_layout", "selected_parts");
                return false;
            }

            if (parts[0].EpisodeNumber is not null)
            {
                issues.Record("invalid_layout", "selected_parts[0].episode_number");
                return false;
            }

            return true;
        }

        if (parts.Any(part => part.EpisodeNumber is null))
        {
            issues.Record("invalid_layout", "selected_parts");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the declared file set against the exact frozen templates: every entry must match the
    /// template its kind claims, and every required template must be covered exactly once with no
    /// unselected cid and no extra or duplicate object.
    /// </summary>
    public static bool ValidateFiles(
        MediaPackageLayout layout,
        string mediaExtension,
        IReadOnlyList<MediaPackageSelectedPart> parts,
        IReadOnlyList<MediaPackageFileEntry> files,
        MediaPackageIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(issues);

        var episodesByCid = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            if (part.EpisodeNumber is { } episode)
            {
                episodesByCid[part.Cid] = episode;
            }
        }

        var videos = new Dictionary<string, int>(StringComparer.Ordinal);
        var nfos = new Dictionary<string, int>(StringComparer.Ordinal);
        var thumbs = new Dictionary<string, int>(StringComparer.Ordinal);
        var matched = false;
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var location = $"files[{index}]";
            if (!MatchesTemplate(
                    layout,
                    mediaExtension,
                    episodesByCid,
                    file,
                    issues,
                    out var role))
            {
                matched = true;
                continue;
            }

            switch (role)
            {
                case EntryRole.SingleVideo:
                case EntryRole.MultipartVideo:
                    videos[file.Cid!] = videos.GetValueOrDefault(file.Cid!) + 1;
                    break;
                case EntryRole.SingleMovieNfo:
                case EntryRole.MultipartEpisodeNfo:
                    nfos[file.Cid ?? string.Empty] = nfos.GetValueOrDefault(file.Cid ?? string.Empty) + 1;
                    break;
                case EntryRole.EpisodeThumb:
                    thumbs[file.Cid!] = thumbs.GetValueOrDefault(file.Cid!) + 1;
                    break;
                case EntryRole.MultipartShowNfo:
                case EntryRole.Source:
                case EntryRole.Poster:
                    matched = true;
                    break;
                default:
                    issues.Record("invalid_file_set", location);
                    matched = true;
                    break;
            }
        }

        return ValidateCardinality(layout, parts, files, videos, nfos, thumbs, issues) && !matched;
    }

    /// <summary>
    /// Decides whether one entry matches the template its kind claims, including the exact file name,
    /// the media extension, the cid of its own part and the episode-number mapping.
    /// </summary>
    public static bool MatchesTemplate(
        MediaPackageLayout layout,
        string mediaExtension,
        IReadOnlyDictionary<string, int> episodesByCid,
        MediaPackageFileEntry file,
        MediaPackageIssueSink issues,
        out EntryRole role)
    {
        ArgumentNullException.ThrowIfNull(episodesByCid);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(issues);
        role = EntryRole.Inapplicable;

        // The raw spelling is refused before the value is used for anything, so a traversal or an
        // alternate separator can never be normalized into a layout match.
        if (MediaPackagePathPolicy.Validate(file.Path) is not null)
        {
            issues.Record("invalid_path", "path");
            return false;
        }

        var name = file.Path.Split('/')[^1];
        if (layout == MediaPackageLayout.SinglePart)
        {
            return MatchesSingle(file, mediaExtension, name, issues, out role);
        }

        return MatchesMultipart(file, mediaExtension, episodesByCid, name, issues, out role);
    }

    private static bool MatchesSingle(
        MediaPackageFileEntry file,
        string mediaExtension,
        string name,
        MediaPackageIssueSink issues,
        out EntryRole role)
    {
        role = EntryRole.Inapplicable;
        switch (file.Kind)
        {
            case MediaPackageFileKind.Video:
                if (file.Cid is null || name != "video." + mediaExtension)
                {
                    issues.Record("invalid_file_set", "layout");
                    return false;
                }

                role = EntryRole.SingleVideo;
                return true;
            case MediaPackageFileKind.Nfo:
                if (file.Cid is not null || name != "movie.nfo")
                {
                    issues.Record("invalid_file_set", "layout");
                    return false;
                }

                role = EntryRole.SingleMovieNfo;
                return true;
            case MediaPackageFileKind.Source:
                if (file.Cid is not null || name != SourcePath)
                {
                    issues.Record("invalid_file_set", "layout");
                    return false;
                }

                role = EntryRole.Source;
                return true;
            case MediaPackageFileKind.Poster:
                if (file.Cid is not null || name is not ("poster.jpg" or "poster.png"))
                {
                    issues.Record("invalid_file_set", "layout");
                    return false;
                }

                role = EntryRole.Poster;
                return true;
            default:
                // A single package has no episode-scoped entry at all.
                issues.Record("invalid_file_set", "layout");
                return false;
        }
    }

    private static bool MatchesMultipart(
        MediaPackageFileEntry file,
        string mediaExtension,
        IReadOnlyDictionary<string, int> episodesByCid,
        string name,
        MediaPackageIssueSink issues,
        out EntryRole role)
    {
        role = EntryRole.Inapplicable;
        switch (file.Kind)
        {
            case MediaPackageFileKind.Nfo when file.Cid is null && name == "tvshow.nfo":
                role = EntryRole.MultipartShowNfo;
                return true;
            case MediaPackageFileKind.Source when file.Cid is null && name == SourcePath:
                role = EntryRole.Source;
                return true;
            case MediaPackageFileKind.Poster when file.Cid is null && name is "poster.jpg" or "poster.png":
                role = EntryRole.Poster;
                return true;
            case MediaPackageFileKind.Video:
            case MediaPackageFileKind.Nfo:
            case MediaPackageFileKind.EpisodeThumb:
                return MatchesEpisodeEntry(
                    file,
                    mediaExtension,
                    episodesByCid,
                    name,
                    issues,
                    out role);
            default:
                issues.Record("invalid_file_set", "layout");
                return false;
        }
    }

    private static bool MatchesEpisodeEntry(
        MediaPackageFileEntry file,
        string mediaExtension,
        IReadOnlyDictionary<string, int> episodesByCid,
        string name,
        MediaPackageIssueSink issues,
        out EntryRole role)
    {
        role = EntryRole.Inapplicable;
        if (file.Cid is null
            || !episodesByCid.TryGetValue(file.Cid, out var episode)
            || !TryReadEpisodeStem(name, episode, file.Cid, out var stem))
        {
            issues.Record("invalid_file_set", "layout");
            return false;
        }

        switch (file.Kind)
        {
            case MediaPackageFileKind.Video when name == stem + "." + mediaExtension:
                role = EntryRole.MultipartVideo;
                return true;
            case MediaPackageFileKind.Nfo when name == stem + ".nfo":
                role = EntryRole.MultipartEpisodeNfo;
                return true;
            case MediaPackageFileKind.EpisodeThumb when name is var _ && IsThumbName(name, stem):
                role = EntryRole.EpisodeThumb;
                return true;
            default:
                issues.Record("invalid_file_set", "layout");
                return false;
        }
    }

    /// <summary>
    /// Parses the frozen multipart stem <c>S01E{episode}-cid-{cid}</c>. The episode number is at least
    /// two digits with no leading zero beyond that, and the cid is the exact declared value.
    /// </summary>
    private static bool TryReadEpisodeStem(
        string name,
        int episode,
        string cid,
        out string stem)
    {
        stem = string.Empty;
        if (!name.StartsWith("S01E", StringComparison.Ordinal))
        {
            return false;
        }

        var marker = name.IndexOf("-cid-", StringComparison.Ordinal);
        if (marker <= "S01E".Length)
        {
            return false;
        }

        var digits = name["S01E".Length..marker];
        if (digits.Length < 2
            || digits[0] == '0' && digits.Length > 2
            || !digits.All(char.IsAsciiDigit)
            || !int.TryParse(digits, out var parsed)
            || parsed != episode)
        {
            return false;
        }

        var declared = name[(marker + "-cid-".Length)..];
        if (declared.Length <= cid.Length
            || !declared.StartsWith(cid, StringComparison.Ordinal)
            || !IsContinuationBoundary(declared[cid.Length]))
        {
            return false;
        }

        stem = name[..(marker + "-cid-".Length + cid.Length)];
        return true;
    }

    private static bool IsThumbName(string name, string stem) =>
        name.Length > stem.Length
        && name.StartsWith(stem, StringComparison.Ordinal)
        && name[stem.Length..] is "-thumb.jpg" or "-thumb.png";

    /// <summary>
    /// The character that must follow a cid inside a file name: anything else would make
    /// <c>cid-1012</c> look like <c>cid-101</c>.
    /// </summary>
    private static bool IsContinuationBoundary(char value) =>
        value is '.' or '-';

    private static bool ValidateCardinality(
        MediaPackageLayout layout,
        IReadOnlyList<MediaPackageSelectedPart> parts,
        IReadOnlyList<MediaPackageFileEntry> files,
        IReadOnlyDictionary<string, int> videos,
        IReadOnlyDictionary<string, int> nfos,
        IReadOnlyDictionary<string, int> thumbs,
        MediaPackageIssueSink issues)
    {
        var valid = true;
        foreach (var part in parts)
        {
            var videoCount = videos.GetValueOrDefault(part.Cid);
            var nfoCount = layout == MediaPackageLayout.SinglePart
                ? nfos.GetValueOrDefault(string.Empty)
                : nfos.GetValueOrDefault(part.Cid);
            if (videoCount != 1 || nfoCount != 1 || thumbs.GetValueOrDefault(part.Cid) > 1)
            {
                issues.Record("invalid_file_set", "selected_parts");
                valid = false;
            }
        }

        if (layout == MediaPackageLayout.Multipart)
        {
            var showNfo = files.Count(file =>
                file.Kind == MediaPackageFileKind.Nfo
                && file.Cid is null
                && string.Equals(file.Path, "tvshow.nfo", StringComparison.Ordinal));
            if (showNfo != 1)
            {
                issues.Record("invalid_file_set", "tvshow.nfo");
                valid = false;
            }
        }

        var sources = files.Count(file =>
            file.Kind == MediaPackageFileKind.Source
            && string.Equals(file.Path, SourcePath, StringComparison.Ordinal));
        if (sources != 1)
        {
            issues.Record("invalid_file_set", SourcePath);
            valid = false;
        }

        if (files.Count(file => file.Kind == MediaPackageFileKind.Poster) > 1)
        {
            issues.Record("invalid_file_set", "poster");
            valid = false;
        }

        return valid;
    }
}
