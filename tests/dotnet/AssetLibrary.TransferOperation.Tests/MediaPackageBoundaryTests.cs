using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;
using AssetLibrary.Modules.OperationTrash.Infrastructure;

namespace AssetLibrary.TransferOperation.Tests;

/// <summary>
/// Static path boundary: raw manifest path refusal, case-folded collisions, containment and reparse
/// point refusal. The link tests are attempted for real in the sandbox and reported as skipped when
/// this machine refuses to create one.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "A boundary test must name both sides of every boundary it proves: the raw manifest path vocabulary, the path policy, the trusted root type and the composition that runs the real preflight.")]
[TestClass]
public sealed class MediaPackageBoundaryTests
{
    private static readonly string[] RefusedPaths =
    [
        "../movie.nfo",
        "Season 01/../../video.mp4",
        "dir\\movie.nfo",
        "movie.nfo:payload",
        "C:/movie.nfo",
        "\\\\server\\share\\movie.nfo",
        "/movie.nfo",
        "movie.nfo.",
        "movie.nfo ",
        " movie.nfo",
        "con.nfo",
        "CON",
        "Season 01/com1.mp4",
        "Season 01/Season 02/video.mp4",
        "Season 01//video.mp4",
        "Season 01/./video.mp4",
        "movie\tnfo",
    ];

    private static readonly string[] AcceptedPaths =
    [
        "movie.nfo",
        "tvshow.nfo",
        "source.json",
        "poster.jpg",
        "poster.png",
        "video.mp4",
        "video.mkv",
        "Season 01/S01E01-cid-101.mp4",
        "Season 01/S01E01-cid-101.nfo",
        "Season 01/S01E02-cid-202-thumb.png",
        "season 01/S01E01-cid-101.mp4",
    ];

    private static readonly string[] RelativeEscapeAttempts =
    [
        "..",
        "../escape",
        "pkg/../../escape",
        "pkg//child",
        ".\\child",
    ];

    [TestMethod]
    public void RawPathPolicyRefusesTraversalSeparatorsAdsAndDeviceNames()
    {
        foreach (var path in RefusedPaths)
        {
            Assert.AreEqual("invalid_path", MediaPackagePathPolicy.Validate(path), path);
        }

        foreach (var path in AcceptedPaths)
        {
            Assert.IsNull(MediaPackagePathPolicy.Validate(path), path);
        }

        var oversized = new string('a', MediaPackagePathPolicy.MaximumPathLength + 1);
        Assert.AreEqual("invalid_path", MediaPackagePathPolicy.Validate(oversized));
    }

    [TestMethod]
    public void CaseFoldedCollisionsAreDetected()
    {
        Assert.IsTrue(MediaPackagePathPolicy.Collides("movie.nfo", "Movie.NFO"));
        Assert.IsTrue(
            MediaPackagePathPolicy.Collides("Season 01/S01E01-cid-101.mp4", "season 01/S01E01-cid-101.mp4"));
        Assert.IsTrue(MediaPackagePathPolicy.Collides("poster.png", "poster.png/child.png"));
        Assert.IsFalse(
            MediaPackagePathPolicy.Collides("Season 01/S01E01-cid-101.mp4", "Season 01/S01E02-cid-202.mp4"));
        Assert.IsFalse(MediaPackagePathPolicy.Collides("movie.nfo", "poster.nfo"));
    }

    [TestMethod]
    public void DuplicateDeclaredPathsAreRejectedByTheReader()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var composition = sandbox.Compose();
        var original = MediaPackageSandbox.ReadExamples()[0].ManifestBytes;

        // The original poster entry is kept and a second complete entry is appended to the files ARRAY that
        // differs only in case, so the manifest really declares two paths that would materialize as one
        // object on a case-insensitive volume. Appending through the array keeps the document valid JSON:
        // splicing an object into the property list instead produced `"path":"poster.png",{...`, which is not
        // JSON at all and made the test prove nothing about duplicate paths.
        var root = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(original)!;
        var files = root["files"]!.AsArray();
        var poster = files
            .Select(node => (System.Text.Json.Nodes.JsonObject)node!)
            .Single(file => file["path"]!.GetValue<string>() == "poster.png");
        files.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["path"] = "Poster.PNG",
            ["kind"] = poster["kind"]!.GetValue<string>(),
            ["cid"] = null,
            ["size_bytes"] = poster["size_bytes"]!.GetValue<long>(),
            ["sha256"] = poster["sha256"]!.GetValue<string>(),
        });

        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            root.WriteTo(writer);
        }

        var bytes = stream.ToArray();

        // The mutated input must still parse and must really carry both entries, so the refusal below can
        // only come from the duplicate-path rule and not from a broken document.
        var reparsed = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        var declaredPaths = reparsed["files"]!.AsArray()
            .Select(node => node!["path"]!.GetValue<string>())
            .ToArray();
        CollectionAssert.Contains(declaredPaths, "poster.png");
        CollectionAssert.Contains(declaredPaths, "Poster.PNG");
        Assert.HasCount(files.Count, declaredPaths);

        var result = composition.Reader.Read(
            bytes,
            new AssetLibrary.Modules.TransferSync.Contracts.Sha256Digest(
                Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(bytes))));

        Assert.IsNull(result.Manifest);
        CollectionAssert.Contains(
            result.Issues.Select(issue => issue.Code).ToArray(),
            "duplicate_path");
    }

    [TestMethod]
    public void BoundaryContainsAndOverlapsArePrefixSafe()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var root = new CanonicalLibraryRoot(sandbox.LibraryRoot, RootPathComparison.CaseInsensitive);
        Assert.IsTrue(MediaPackagePathBoundary.TryOpen(
            sandbox.LibraryRoot,
            root,
            out var boundary,
            out var fault), fault.ToString());
        Assert.IsNotNull(boundary);
        Assert.IsFalse(boundary.Overlaps(sandbox.StagingRoot, sandbox.LibraryRoot));

        var sibling = Path.Combine(Path.GetDirectoryName(sandbox.LibraryRoot)!, "library-sibling");
        Assert.IsFalse(
            boundary.Contains(sibling, sandbox.LibraryRoot),
            "A sibling with a shared name prefix is not contained.");
        Assert.IsTrue(
            boundary.Contains(
                Path.Combine(sandbox.LibraryRoot, "pkg"),
                sandbox.LibraryRoot));
        Assert.IsTrue(boundary.Overlaps(sandbox.LibraryRoot, sandbox.LibraryRoot));
        Assert.IsTrue(boundary.Overlaps(
            sandbox.LibraryRoot,
            Path.Combine(sandbox.LibraryRoot, "pkg")));
    }

    [TestMethod]
    public void BoundaryRefusesAlternateSeparatorsAndParentTraversal()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var root = new CanonicalLibraryRoot(sandbox.LibraryRoot, RootPathComparison.CaseInsensitive);
        Assert.IsTrue(MediaPackagePathBoundary.TryOpen(
            sandbox.LibraryRoot,
            root,
            out var boundary,
            out _));

        foreach (var relative in RelativeEscapeAttempts)
        {
            Assert.IsFalse(
                boundary!.TryResolveChild(sandbox.LibraryRoot, relative, out _, out var fault),
                relative);
            Assert.AreNotEqual(MediaPackagePathFault.None, fault);
        }

        Assert.IsTrue(
            boundary!.TryResolveChild(
                sandbox.LibraryRoot,
                "pkg/child.nfo",
                out var resolved,
                out _));
        Assert.IsTrue(boundary.Contains(resolved, sandbox.LibraryRoot));
    }

    [TestMethod]
    public void BoundaryAcceptsPosixNestedPathsAndRefusesEveryOtherSeparator()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var root = new CanonicalLibraryRoot(sandbox.LibraryRoot, RootPathComparison.CaseInsensitive);
        Assert.IsTrue(MediaPackagePathBoundary.TryOpen(
            sandbox.LibraryRoot,
            root,
            out var boundary,
            out var fault), fault.ToString());

        // The manifest vocabulary is POSIX: a nested path is resolved on every platform, and the
        // resolved path stays contained in the trusted root.
        Assert.IsTrue(
            boundary!.TryResolveChild(
                sandbox.LibraryRoot,
                "Season 01/S01E01-cid-101.mp4",
                out var nested,
                out var nestedFault),
            nestedFault.ToString());
        Assert.IsTrue(boundary.Contains(nested, sandbox.LibraryRoot));

        // The manifest vocabulary is POSIX on every platform: a forward slash is the accepted separator
        // and is converted to the native one before real access, while a backslash is refused everywhere
        // as an alternate spelling. A trailing separator is not part of a frozen file path.
        Assert.IsFalse(
            boundary.TryResolveChild(sandbox.LibraryRoot, "Season 01\\S01E01-cid-101.mp4", out _, out var backslashFault));
        Assert.AreEqual(MediaPackagePathFault.Unsafe, backslashFault);
        if (OperatingSystem.IsWindows())
        {
            Assert.IsFalse(
                boundary.TryResolveChild(sandbox.LibraryRoot, "Season 01/S01E01-cid-101.mp4/", out _, out _),
                "A trailing separator is not part of a frozen file path.");
        }
    }

    [TestMethod]
    public void WindowsCanonicalRootIsNotRefusedForItsSeparator()
    {
        using var sandbox = MediaPackageSandbox.Create();

        // The trusted root arrives in its canonical forward-slash form while the local path arrives in
        // the platform-native form. Opening the boundary must accept that pair, and containment must
        // still be decided in the canonical form: a normal Windows directory is never refused for its
        // separator, and a sibling with a shared prefix is still outside.
        var canonical = new CanonicalLibraryRoot(sandbox.LibraryRoot, RootPathComparison.CaseInsensitive);
        Assert.IsTrue(
            MediaPackagePathBoundary.TryOpen(
                sandbox.LibraryRoot,
                canonical,
                out var boundary,
                out var fault),
            fault.ToString());
        Assert.IsNotNull(boundary);
        Assert.IsTrue(boundary.Contains(sandbox.LibraryRoot, canonical.Value));
        Assert.IsTrue(boundary.Contains(Path.Combine(sandbox.LibraryRoot, "pkg"), canonical.Value));
        Assert.IsTrue(boundary.Contains(sandbox.LibraryRoot, sandbox.LibraryRoot));
        Assert.IsFalse(
            boundary.Contains(
                Path.Combine(Path.GetDirectoryName(sandbox.LibraryRoot)!, "library-sibling"),
                canonical.Value));

        // A root that names another directory is still refused.
        Assert.IsFalse(
            MediaPackagePathBoundary.TryOpen(
                Path.Combine(sandbox.Root, "outside"),
                canonical,
                out _,
                out _));
    }

    [TestMethod]
    public void OverlappingStagingAndTargetRootsAreRefused()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = MediaPackageSandbox.ReadExamples()[0];
        sandbox.WritePackage(example);
        var composition = sandbox.Compose();

        // The staging root becomes the target library root: one contains the other, which must be
        // refused before any file is read.
        composition.Scope.GrantRaw(
            new MediaPackageInspectionScope(
                "scope-rev-overlap",
                DateTimeOffset.UtcNow.AddMinutes(10),
                StorageAvailability.Online,
                sandbox.StagingToken,
                sandbox.StagingToken));
        var before = sandbox.SnapshotTree();

        var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
        CollectionAssert.Contains(
            report.Issues.Select(issue => issue.Code).ToArray(),
            "unsafe_path");
        CollectionAssert.AreEqual(before.ToArray(), sandbox.SnapshotTree().ToArray());
    }

    [TestMethod]
    public void LinkedPackageRootDirectoryIsRefused()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = MediaPackageSandbox.ReadExamples()[0];
        var packageRoot = sandbox.WritePackage(example);
        var outside = Path.Combine(sandbox.Root, "outside");
        Directory.CreateDirectory(outside);

        // Move the real package out of the way and link the declared staging_ref at it. Writing a link
        // needs a privilege this process may not hold; that is reported, not hidden.
        var moved = Path.Combine(outside, Path.GetFileName(packageRoot));
        Directory.Move(packageRoot, moved);
        if (!TryCreateDirectoryLink(packageRoot, moved))
        {
            Assert.Inconclusive(
                "This machine refuses to create a directory symbolic link for the current process, "
                + "so the linked-root refusal could not be exercised here (needs_validation).");
        }

        try
        {
            var composition = sandbox.Compose();
            var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
            CollectionAssert.Contains(
                report.Issues.Select(issue => issue.Code).ToArray(),
                "unsafe_path");
        }
        finally
        {
            Directory.Delete(packageRoot);
        }
    }

    [TestMethod]
    public void LinkedPackageFileIsRefusedRatherThanFollowed()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var example = MediaPackageSandbox.ReadExamples()[0];
        var packageRoot = sandbox.WritePackage(example);
        var video = Path.Combine(packageRoot, "video.mp4");
        var payload = Path.Combine(sandbox.Root, "outside-video.mp4");
        File.Move(video, payload);
        if (!TryCreateFileLink(video, payload))
        {
            Assert.Inconclusive(
                "This machine refuses to create a file symbolic link for the current process, so the "
                + "linked-file refusal could not be exercised here (needs_validation).");
        }

        try
        {
            var composition = sandbox.Compose();
            var report = composition.InspectAsync(example).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(MediaPackageInspectionStatus.Rejected, report.Status);
            CollectionAssert.Contains(
                report.Issues.Select(issue => issue.Code).ToArray(),
                "unsafe_path");
        }
        finally
        {
            File.Delete(video);
        }
    }

    [TestMethod]
    public void MultipartEpisodeDirectoryIsRefusedWhenEveryEntryLeavesSeason01()
    {
        using var sandbox = MediaPackageSandbox.Create();
        var composition = sandbox.Compose();
        var multipart = MediaPackageSandbox.ReadExamples().Single(item => item.Name == "multipart");

        // Every episode entry moves together, so the declared set stays internally consistent and only the
        // frozen "Season 01/" directory is violated. The reader must refuse it: the directory is part of
        // the contract, not a detail the manifest is allowed to choose.
        foreach (var (from, to) in new[]
        {
            ("Season 01/", "Season 99/"),
            ("Season 01/", string.Empty),
        })
        {
            var bytes = MoveEveryEpisodeEntry(multipart.ManifestBytes, from, to);
            var result = composition.Reader.Read(
                bytes,
                new AssetLibrary.Modules.TransferSync.Contracts.Sha256Digest(
                    Convert.ToHexStringLower(
                        System.Security.Cryptography.SHA256.HashData(bytes))));

            Assert.IsNull(result.Manifest, $"Moving every episode entry to '{to}' must be refused.");
            CollectionAssert.Contains(
                result.FailureCode is null
                    ? result.Issues.Select(issue => issue.Code).ToArray()
                    : [result.FailureCode],
                "invalid_file_set");
        }
    }

    /// <summary>
    /// Rewrites every episode entry from one directory prefix to another and leaves the root-level entries
    /// alone, so only the season directory differs from the frozen layout.
    /// </summary>
    private static byte[] MoveEveryEpisodeEntry(byte[] manifestBytes, string from, string to)
    {
        var root = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(manifestBytes)!;
        foreach (var file in root["files"]!.AsArray())
        {
            var path = file!["path"]!.GetValue<string>();
            if (path.StartsWith(from, StringComparison.Ordinal))
            {
                file["path"] = to + path[from.Length..];
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            root.WriteTo(writer);
        }

        return stream.ToArray();
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateFileLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
