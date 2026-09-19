using System.Text.Json;
using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Domain;

/// <summary>
/// Pure orchestration of the manifest verdict: it declares the frozen budgets, delegates the JSON shape
/// and identity reading to <see cref="MediaPackageShapePolicy"/>, the fixed layout and file-set templates
/// to <see cref="MediaPackageLayoutPolicy"/>, compares the declared set with the real listing and computes
/// the server-side target directory name. It reads no file, no directory, no clock and no environment.
/// </summary>
public static class MediaPackagePolicy
{
    public const int MinimumFiles = MediaPackageShapePolicy.MinimumFiles;
    public const int MaximumFiles = MediaPackageShapePolicy.MaximumFiles;
    public const int MaximumSelectedParts = MediaPackageShapePolicy.MaximumSelectedParts;
    public const int MaximumEpisodeNumber = MediaPackageShapePolicy.MaximumEpisodeNumber;

    public const long MaximumFileSizeBytes = MediaPackageShapePolicy.MaximumFileSizeBytes;
    public const long MaximumDeclaredTotalBytes = 1_099_511_627_776;
    public const long MaximumNfoOrSourceBytes = MediaPackageShapePolicy.MaximumNfoOrSourceBytes;
    public const long MaximumImageBytes = MediaPackageShapePolicy.MaximumImageBytes;

    /// <summary>
    /// Validates the whole manifest and returns the immutable manifest, or null when the document is
    /// unacceptable. <paramref name="root"/> is the parsed strict JSON root of the received bytes; only a
    /// non-null result may continue to authorization.
    /// </summary>
    public static MediaPackageManifest? Validate(JsonElement root, MediaPackageIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var shape = MediaPackageShapePolicy.Read(root, issues);
        if (shape is null)
        {
            return null;
        }

        ValidateSelectedParts(shape, issues);
        ValidateFiles(shape, issues);
        ValidateDeclaredByteBudget(shape.Files, issues);
        if (issues.HasIssues)
        {
            return null;
        }

        // Defensive snapshot: the validated state can never be mutated afterwards through the collection
        // the caller passed in, because this manifest owns fresh read-only copies.
        return new MediaPackageManifest(
            shape.PackageId,
            shape.StagingRef,
            shape.LibraryId,
            shape.Bvid,
            shape.Layout,
            shape.MediaExtension,
            [.. shape.SelectedParts],
            [.. shape.Files]);
    }

    /// <summary>
    /// Sum of the declared file sizes. This is the declaration budget, not an observed cost.
    /// </summary>
    public static long DeclaredByteCount(IReadOnlyList<MediaPackageFileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        long total = 0;
        foreach (var file in files)
        {
            total = file.SizeBytes > long.MaxValue - total ? long.MaxValue : total + file.SizeBytes;
        }

        return total;
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

    private static void ValidateSelectedParts(MediaPackageShape shape, MediaPackageIssueSink issues)
    {
        _ = MediaPackageLayoutPolicy.ValidateSelectedParts(shape.Layout, shape.SelectedParts, issues);
    }

    private static void ValidateFiles(MediaPackageShape shape, MediaPackageIssueSink issues)
    {
        foreach (var file in shape.Files)
        {
            var pathFailure = MediaPackagePathPolicy.Validate(file.Path);
            if (pathFailure is not null)
            {
                // The raw spelling is unverified, so the report carries the fixed field position only.
                issues.Record(pathFailure, "path");
            }
        }

        ValidatePerKindSizeCeilings(shape.Files, issues);
        ValidateDuplicatePaths(shape.Files, issues);
        _ = MediaPackageLayoutPolicy.ValidateFiles(
            shape.Layout,
            shape.MediaExtension,
            shape.SelectedParts,
            shape.Files,
            issues);
    }

    /// <summary>
    /// Per-kind declaration ceilings: an nfo or source entry is at most 4 MiB and an image at most 32 MiB,
    /// on top of the per-file ceiling the shape reader already applied.
    /// </summary>
    private static void ValidatePerKindSizeCeilings(
        IReadOnlyList<MediaPackageFileEntry> files,
        MediaPackageIssueSink issues)
    {
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            if (file.Kind == MediaPackageFileKind.Video)
            {
                continue;
            }

            var ceiling = file.Kind is MediaPackageFileKind.Nfo or MediaPackageFileKind.Source
                ? MaximumNfoOrSourceBytes
                : MaximumImageBytes;
            if (file.SizeBytes > ceiling)
            {
                issues.Record("invalid_file_set", $"files[{index}]");
            }
        }
    }

    /// <summary>
    /// Case-folded collisions: a duplicate file and a file/directory prefix collision are both unsafe,
    /// because they would materialize as one object on a case-insensitive volume.
    /// </summary>
    private static void ValidateDuplicatePaths(
        IReadOnlyList<MediaPackageFileEntry> files,
        MediaPackageIssueSink issues)
    {
        for (var left = 0; left < files.Count; left++)
        {
            for (var right = left + 1; right < files.Count; right++)
            {
                if (MediaPackagePathPolicy.Collides(files[left].Path, files[right].Path))
                {
                    issues.Record("duplicate_path", $"files[{right}]");
                }
            }
        }
    }

    private static void ValidateDeclaredByteBudget(
        IReadOnlyList<MediaPackageFileEntry> files,
        MediaPackageIssueSink issues)
    {
        if (DeclaredByteCount(files) > MaximumDeclaredTotalBytes)
        {
            issues.Record("budget_exceeded", "files");
        }
    }
}
