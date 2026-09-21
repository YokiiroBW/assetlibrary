using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Domain;

/// <summary>
/// Pure layout policy: it turns <c>layout</c>, <c>media_extension</c> and <c>selected_parts</c> into the
/// exact required and optional file templates of the frozen candidate, and decides whether each declared
/// entry matches the template its <c>kind</c> claims. Templates are matched against the <b>complete</b>
/// package-relative path, so a directory component can never be dropped and a file moved into a
/// subdirectory can never be accepted. It decodes no JSON, reads no file and touches no clock.
/// </summary>
internal static class MediaPackageLayoutPolicy
{
    /// <summary>
    /// Which frozen template one declared entry was recognised as, or that it matched none.
    /// </summary>
    internal enum EntryRole
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

    internal const string SourcePath = "source.json";

    internal const string ShowNfoPath = "tvshow.nfo";

    internal const string MovieNfoPath = "movie.nfo";

    /// <summary>
    /// The one directory every multipart episode entry must share. The name is not invented here: it is
    /// the directory the declared episode paths themselves agree on, so the templates, the declared set
    /// and the real listing all describe the same directory. A second episode directory is refused.
    /// </summary>
    internal static string? EpisodeDirectory(IReadOnlyList<MediaPackageFileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var found = false;
        var directory = string.Empty;
        foreach (var file in files)
        {
            if (!IsEpisodeEntry(file))
            {
                continue;
            }

            var separator = file.Path.LastIndexOf('/');
            var candidate = separator < 0 ? string.Empty : file.Path[..separator];
            if (!found)
            {
                directory = candidate;
                found = true;
                continue;
            }

            if (!string.Equals(directory, candidate, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return directory;
    }

    /// <summary>
    /// True for a declared entry that the multipart layout pins to one episode part, and therefore to one
    /// episode directory.
    /// </summary>
    private static bool IsEpisodeEntry(MediaPackageFileEntry file) =>
        file.Kind is MediaPackageFileKind.Video or MediaPackageFileKind.EpisodeThumb
        || (file.Kind == MediaPackageFileKind.Nfo && file.Cid is not null);

    private const string EpisodePrefix = "S01E";

    private const string CidMarker = "-cid-";

    /// <summary>
    /// Validates the selected parts against the layout: single admits exactly one part whose episode
    /// number is null, multipart requires an episode number on every selected part.
    /// </summary>
    internal static bool ValidateSelectedParts(
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
    /// Validates the declared file set against the complete frozen templates. Every entry must match the
    /// template its kind claims at its full path, no two entries may claim the same role, and every
    /// required template must be covered exactly once, so an extra entry, a duplicate role, a moved file
    /// and an unselected cid are all refused.
    /// </summary>
    internal static bool ValidateFiles(
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

        // The frozen roles are one-to-one for a single package. A multipart package repeats the three
        // episode-scoped roles once per selected part, so uniqueness is keyed by the part's cid: two
        // entries of the same role in the same part are refused here, while two parts are not a duplicate.
        // Every root-level role stays unique, because a single package carries exactly one of each.
        var covered = new HashSet<(EntryRole Role, string Key)>();

        // A null directory means the declared episode entries disagree about their own directory, which no
        // single template can satisfy: the disagreement itself is one named refusal.
        var sharedDirectory = layout == MediaPackageLayout.Multipart ? EpisodeDirectory(files) : string.Empty;
        var episodeDirectory = sharedDirectory ?? string.Empty;
        var unmatched = 0;
        if (sharedDirectory is null)
        {
            issues.Record("invalid_file_set", "layout");
            unmatched++;
        }

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var location = $"files[{index}]";
            if (MediaPackagePathPolicy.Validate(file.Path) is { } pathFailure)
            {
                // The raw spelling is unverified, so the report carries the fixed field position only.
                issues.Record(pathFailure, "path");
                unmatched++;
                continue;
            }

            if (!MatchesTemplate(
                    layout,
                    mediaExtension,
                    episodesByCid,
                    episodeDirectory,
                    file,
                    out var role))
            {
                issues.Record("invalid_file_set", location);
                unmatched++;
                continue;
            }

            if (!covered.Add((role, RoleKey(role, file.Cid))))
            {
                // Two entries cannot claim the same role for the same part: a second video, nfo, source,
                // poster or thumb is a duplicate of an object the template already places.
                issues.Record("invalid_file_set", location);
                unmatched++;
                continue;
            }

            switch (role)
            {
                case EntryRole.SingleVideo:
                case EntryRole.MultipartVideo:
                    videos[file.Cid!] = videos.GetValueOrDefault(file.Cid!) + 1;
                    break;
                case EntryRole.SingleMovieNfo:
                    nfos[string.Empty] = nfos.GetValueOrDefault(string.Empty) + 1;
                    break;
                case EntryRole.MultipartEpisodeNfo:
                    nfos[file.Cid!] = nfos.GetValueOrDefault(file.Cid!) + 1;
                    break;
                case EntryRole.EpisodeThumb:
                    thumbs[file.Cid!] = thumbs.GetValueOrDefault(file.Cid!) + 1;
                    break;
                default:
                    break;
            }
        }

        var complete = ValidateCardinality(layout, parts, files, videos, nfos, thumbs, issues);
        return complete && unmatched == 0;
    }

    /// <summary>
    /// Decides whether one entry matches the template its kind claims, at its complete path, including
    /// the media extension, the cid of its own part and the episode-number mapping.
    /// </summary>
    private static bool MatchesTemplate(
        MediaPackageLayout layout,
        string mediaExtension,
        IReadOnlyDictionary<string, int> episodesByCid,
        string episodeDirectory,
        MediaPackageFileEntry file,
        out EntryRole role)
    {
        role = EntryRole.Inapplicable;
        return layout == MediaPackageLayout.SinglePart
            ? MatchesSingle(file, mediaExtension, out role)
            : MatchesMultipart(file, mediaExtension, episodesByCid, episodeDirectory, out role);
    }

    private static bool MatchesSingle(
        MediaPackageFileEntry file,
        string mediaExtension,
        out EntryRole role)
    {
        role = EntryRole.Inapplicable;
        switch (file.Kind)
        {
            case MediaPackageFileKind.Video:
                if (file.Cid is null || !IsAtRoot(file.Path, "video." + mediaExtension))
                {
                    return false;
                }

                role = EntryRole.SingleVideo;
                return true;
            case MediaPackageFileKind.Nfo:
                if (file.Cid is not null || !IsAtRoot(file.Path, MovieNfoPath))
                {
                    return false;
                }

                role = EntryRole.SingleMovieNfo;
                return true;
            case MediaPackageFileKind.Source:
                if (file.Cid is not null || !IsAtRoot(file.Path, SourcePath))
                {
                    return false;
                }

                role = EntryRole.Source;
                return true;
            case MediaPackageFileKind.Poster:
                if (file.Cid is not null || !IsRootPoster(file.Path))
                {
                    return false;
                }

                role = EntryRole.Poster;
                return true;
            default:
                // A single package has no episode-scoped entry at all.
                return false;
        }
    }

    private static bool MatchesMultipart(
        MediaPackageFileEntry file,
        string mediaExtension,
        IReadOnlyDictionary<string, int> episodesByCid,
        string episodeDirectory,
        out EntryRole role)
    {
        role = EntryRole.Inapplicable;
        switch (file.Kind)
        {
            case MediaPackageFileKind.Nfo when file.Cid is null && IsAtRoot(file.Path, ShowNfoPath):
                role = EntryRole.MultipartShowNfo;
                return true;
            case MediaPackageFileKind.Source when file.Cid is null && IsAtRoot(file.Path, SourcePath):
                role = EntryRole.Source;
                return true;
            case MediaPackageFileKind.Poster when file.Cid is null && IsRootPoster(file.Path):
                role = EntryRole.Poster;
                return true;
            case MediaPackageFileKind.Video:
            case MediaPackageFileKind.Nfo:
            case MediaPackageFileKind.EpisodeThumb:
                return MatchesEpisodeEntry(
                    file,
                    mediaExtension,
                    episodesByCid,
                    episodeDirectory,
                    out role);
            default:
                return false;
        }
    }

    private static bool MatchesEpisodeEntry(
        MediaPackageFileEntry file,
        string mediaExtension,
        IReadOnlyDictionary<string, int> episodesByCid,
        string episodeDirectory,
        out EntryRole role)
    {
        role = EntryRole.Inapplicable;
        if (file.Cid is null
            || !episodesByCid.TryGetValue(file.Cid, out var episode)
            || !TryReadEpisodeStem(file.Path, episodeDirectory, episode, file.Cid, out var stem))
        {
            return false;
        }

        switch (file.Kind)
        {
            case MediaPackageFileKind.Video when file.Path == stem + "." + mediaExtension:
                role = EntryRole.MultipartVideo;
                return true;
            case MediaPackageFileKind.Nfo when file.Path == stem + ".nfo":
                role = EntryRole.MultipartEpisodeNfo;
                return true;
            case MediaPackageFileKind.EpisodeThumb when IsThumbPath(file.Path, stem):
                role = EntryRole.EpisodeThumb;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The part a repeated role belongs to. Episode-scoped roles are keyed by their own cid, so the same
    /// role in another part is a different object; every root-level role is keyed by one fixed value and
    /// can therefore only appear once.
    /// </summary>
    private static string RoleKey(EntryRole role, string? cid) => role switch
    {
        EntryRole.MultipartVideo or EntryRole.MultipartEpisodeNfo or EntryRole.EpisodeThumb =>
            cid ?? string.Empty,
        _ => string.Empty,
    };

    private static bool IsAtRoot(string path, string name) =>
        string.Equals(path, name, StringComparison.Ordinal);

    private static bool IsRootPoster(string path) =>
        path is "poster.jpg" or "poster.png";

    /// <summary>
    /// Parses the frozen multipart path <c>{directory}/S01E{episode}-cid-{cid}</c> and returns the stem
    /// with its directory, so every accepted episode entry is pinned to the one directory the declared
    /// episode paths agree on. The episode number is at least two digits with no leading zero beyond
    /// that, and the cid is the exact declared value: <c>cid-1012</c> is not <c>cid-101</c>.
    /// </summary>
    private static bool TryReadEpisodeStem(
        string path,
        string episodeDirectory,
        int episode,
        string cid,
        out string stem)
    {
        stem = string.Empty;
        var expectedPrefix = episodeDirectory.Length == 0
            ? EpisodePrefix
            : episodeDirectory + "/" + EpisodePrefix;
        if (!path.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var marker = path.IndexOf(CidMarker, expectedPrefix.Length, StringComparison.Ordinal);
        if (marker <= expectedPrefix.Length)
        {
            return false;
        }

        var digits = path[expectedPrefix.Length..marker];
        if (digits.Length < 2
            || (digits[0] == '0' && digits.Length > 2)
            || !digits.All(char.IsAsciiDigit)
            || !int.TryParse(digits, out var parsed)
            || parsed != episode)
        {
            return false;
        }

        var declared = path[(marker + CidMarker.Length)..];
        if (declared.Length <= cid.Length
            || !declared.StartsWith(cid, StringComparison.Ordinal)
            || !IsContinuationBoundary(declared[cid.Length]))
        {
            return false;
        }

        stem = path[..(marker + CidMarker.Length + cid.Length)];
        return true;
    }

    /// <summary>
    /// True when the character after a candidate cid ends the cid instead of extending it, so a longer
    /// numeric cid can never be read as a shorter declared one.
    /// </summary>
    private static bool IsContinuationBoundary(char character) =>
        character is '.' or '-';

    private static bool IsThumbPath(string path, string stem) =>
        path.Length > stem.Length
        && path.StartsWith(stem, StringComparison.Ordinal)
        && path[stem.Length..] is "-thumb.jpg" or "-thumb.png";

    /// <summary>
    /// Requires exactly the frozen required objects, with the per-cid counts the layout implies, and
    /// records each missing or extra object against a fixed field position.
    /// </summary>
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
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            var location = $"selected_parts[{index}].cid";
            var nfoCount = layout == MediaPackageLayout.SinglePart
                ? nfos.GetValueOrDefault(string.Empty)
                : nfos.GetValueOrDefault(part.Cid);
            if (videos.GetValueOrDefault(part.Cid) != 1
                || nfoCount != 1
                || thumbs.GetValueOrDefault(part.Cid) > 1)
            {
                issues.Record("invalid_file_set", location);
                valid = false;
            }
        }

        if (layout == MediaPackageLayout.Multipart
            && files.Count(file => file.Kind == MediaPackageFileKind.Nfo
                && file.Cid is null
                && IsAtRoot(file.Path, ShowNfoPath)) != 1)
        {
            issues.Record("invalid_file_set", ShowNfoPath);
            valid = false;
        }

        if (files.Count(file => file.Kind == MediaPackageFileKind.Source
                && IsAtRoot(file.Path, SourcePath)) != 1)
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
