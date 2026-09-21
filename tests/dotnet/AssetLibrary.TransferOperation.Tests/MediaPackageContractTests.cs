using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;
using AssetLibrary.Modules.OperationTrash.Infrastructure;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

/// <summary>
/// Contract and manifest-reader behaviour against the frozen candidate fixtures: raw-byte digest,
/// strict JSON shape, identity, layout, file set and the frozen report vocabulary.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "A contract test's type count is the size of the contract it pins: it must name every frozen code, every unverified fact, every manifest field and every reader entry point it asserts on, and those types legitimately live in Contracts, Domain, Application and Infrastructure.")]
[TestClass]
public sealed class MediaPackageContractTests
{
    private const string InvalidManifest = "invalid_manifest";

    private static readonly string[] FrozenCodes =
    [
        "invalid_manifest", "digest_mismatch", "unsupported_version", "invalid_identity",
        "invalid_layout", "invalid_path", "duplicate_path", "invalid_file_set",
        "budget_exceeded", "unauthorized", "scope_changed", "source_missing",
        "source_changed", "unsafe_path", "hash_mismatch", "size_mismatch", "target_exists",
        "target_unavailable", "insufficient_space", "busy", "timeout", "io_failure",
    ];

    private static readonly string[] FrozenUnverifiedFacts =
    [
        "media_decoding", "quality_policy", "metadata_semantics", "publication", "indexing",
        "media_server_import", "production_path_races",
    ];

    [TestMethod]
    public void FixtureFilesMatchThePinnedLfNormalizedHashes()
    {
        var expected = MediaPackageSandbox.ReadFixtureHashes();
        Assert.HasCount(5, expected, "The pinned manifest covers five files.");
        foreach (var (name, digest) in expected)
        {
            var bytes = File.ReadAllBytes(Path.Combine(MediaPackageSandbox.FixtureRoot, name));
            Assert.AreEqual(
                digest,
                Convert.ToHexStringLower(SHA256.HashData(NormalizeLineEndings(bytes))),
                $"{name} does not match the pinned LF-normalized SHA-256.");
        }
    }

    [TestMethod]
    public void BothPositiveCandidatesAreAcceptedWithTheirRawByteDigest()
    {
        using var sandbox = MediaPackageSandbox.Create();
        using var composition = sandbox.Compose();
        var examples = MediaPackageSandbox.ReadExamples();
        Assert.HasCount(2, examples);
        foreach (var example in examples)
        {
            sandbox.WritePackage(example);
            var result = composition.Reader.Read(example.ManifestBytes, example.Digest);
            Assert.IsTrue(result.Succeeded, $"{example.Name} must be accepted.");
            Assert.AreEqual(example.ManifestSha256, result.ManifestDigest.Value);
            Assert.AreEqual(
                example.ManifestSha256,
                Convert.ToHexStringLower(SHA256.HashData(example.ManifestBytes)),
                $"{example.Name} digest is the raw received bytes.");
            Assert.AreEqual(
                example.ExpectedRelativeDirectory,
                MediaPackagePolicy.TargetDirectoryName(result.Manifest!));
        }
    }

    [TestMethod]
    public void EveryNegativeCandidateRaisesItsFrozenCode()
    {
        using var sandbox = MediaPackageSandbox.Create();
        using var composition = sandbox.Compose();
        var negatives = MediaPackageSandbox.ReadNegativeExamples();
        Assert.HasCount(17, negatives);
        foreach (var negative in negatives)
        {
            var result = composition.Reader.Read(negative.ManifestBytes, negative.Digest);
            Assert.IsNull(result.Manifest, $"{negative.Name} must be refused.");
            var codes = result.FailureCode is null
                ? result.Issues.Select(issue => issue.Code).ToArray()
                : new[] { result.FailureCode };
            CollectionAssert.Contains(
                codes,
                negative.ExpectedCode,
                $"{negative.Name} must report {negative.ExpectedCode}.");
        }
    }

    [TestMethod]
    public void RawByteDigestIsNeitherNormalizedNorReserialized()
    {
        using var sandbox = MediaPackageSandbox.Create();
        using var composition = sandbox.Compose();
        var example = MediaPackageSandbox.ReadExamples()[0];

        var crlf = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(example.ManifestBytes)
                .Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.AreNotEqual(example.ManifestSha256, Convert.ToHexStringLower(SHA256.HashData(crlf)));
        var crlfResult = composition.Reader.Read(crlf, example.Digest);
        Assert.IsFalse(crlfResult.Succeeded);
        Assert.AreEqual("digest_mismatch", crlfResult.FailureCode);

        var reserialized = ReorderRootKeys(example.ManifestBytes);
        Assert.AreNotEqual(
            example.ManifestSha256,
            Convert.ToHexStringLower(SHA256.HashData(reserialized)));
        var reserializedResult = composition.Reader.Read(reserialized, example.Digest);
        Assert.IsFalse(reserializedResult.Succeeded);
        Assert.AreEqual("digest_mismatch", reserializedResult.FailureCode);
    }

    [TestMethod]
    public void ReorderedKeysWithCorrectRawDigestAreAccepted()
    {
        using var sandbox = MediaPackageSandbox.Create();
        using var composition = sandbox.Compose();
        var example = MediaPackageSandbox.ReadExamples()[0];
        var reordered = ReorderRootKeys(example.ManifestBytes);
        var digest = new Sha256Digest(Convert.ToHexStringLower(SHA256.HashData(reordered)));

        var result = composition.Reader.Read(reordered, digest);
        Assert.IsTrue(result.Succeeded, "Object member order is not part of the accepted shape.");
        Assert.AreEqual(example.ExpectedRelativeDirectory, MediaPackagePolicy.TargetDirectoryName(result.Manifest!));
    }

    [TestMethod]
    public void NonIntegerNumberAndStringCountLexemesAreRefused()
    {
        using var sandbox = MediaPackageSandbox.Create();
        using var composition = sandbox.Compose();
        var text = Encoding.UTF8.GetString(MediaPackageSandbox.ReadExamples()[0].ManifestBytes);
        const string original = "\"size_bytes\":605";

        foreach (var (name, replacement) in new (string, string)[]
        {
            ("fractional", "\"size_bytes\":605.0"),
            ("exponent", "\"size_bytes\":6.05e2"),
            ("string", "\"size_bytes\":\"605\""),
            ("boolean", "\"size_bytes\":true"),
            ("null", "\"size_bytes\":null"),
            ("leading_zero", "\"size_bytes\":0605"),
        })
        {
            var mutated = text.Replace(original, replacement, StringComparison.Ordinal);
            Assert.AreNotEqual(text, mutated, $"{name} mutation must apply.");
            var bytes = Encoding.UTF8.GetBytes(mutated);
            var result = composition.Reader.Read(
                bytes,
                new Sha256Digest(Convert.ToHexStringLower(SHA256.HashData(bytes))));
            Assert.IsFalse(result.Succeeded, $"{name} must be refused.");
            CollectionAssert.Contains(
                result.Issues.Select(issue => issue.Code).ToArray(),
                InvalidManifest,
                $"{name} must report {InvalidManifest}.");
        }
    }

    [TestMethod]
    public void ReportVocabularyAndUnverifiedFactsAreFrozen()
    {
        CollectionAssert.AreEqual(FrozenCodes, MediaPackagePreflightCodes.All.ToArray());
        CollectionAssert.AreEqual(FrozenUnverifiedFacts, MediaPackageUnverifiedFacts.All.ToArray());
    }

    [TestMethod]
    public void ManifestByteBudgetIsEnforcedBeforeParsing()
    {
        using var composition = MediaPackageSandbox.Create().Compose(
            new MediaPackageInspectionLimits(maximumManifestBytes: 64));
        var example = MediaPackageSandbox.ReadExamples()[0];
        var result = composition.Reader.Read(example.ManifestBytes, example.Digest);
        Assert.IsNull(result.Manifest);
        Assert.AreEqual("budget_exceeded", result.FailureCode);
    }

    [TestMethod]
    public void StagingRootAndLimitsRefuseValuesOutsideTheirCeilings()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new MediaPackageStagingRoot("  ", RootPathComparison.CaseInsensitive));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new MediaPackageInspectionLimits(
                maximumReadBytes: MediaPackageInspectionLimits.MaximumReadBytes + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new MediaPackageInspectionLimits(
                maximumDuration: MediaPackageInspectionLimits.MaximumDuration + TimeSpan.FromSeconds(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new MediaPackageInspectionLimits(
                streamBufferBytes: MediaPackageInspectionLimits.MaximumStreamBufferBytes + 1));

        // A trusted constructor may lower a budget.
        var lowered = new MediaPackageInspectionLimits(maximumFiles: 3, maximumReadBytes: 128);
        Assert.AreEqual(3, lowered.MaximumFileCount);
        Assert.AreEqual(128, lowered.MaximumReadByteCount);
    }

    [TestMethod]
    public void RootLevelFilesAreAcceptedWithAnExplicitNullCid()
    {
        // The frozen layout has four root objects in a single package, and none of them carries a cid.
        // An explicit JSON null is the accepted spelling, so these must not be refused.
        var bytes = Mutate("single", root =>
        {
            foreach (var entry in Files(root))
            {
                var file = entry!.AsObject();
                if (file["kind"]!.GetValue<string>() is not "video")
                {
                    Assert.IsNull(file["cid"], "A root-level entry carries an explicit null cid.");
                }
            }
        });

        var result = ReadWithReader(bytes);

        Assert.IsTrue(result.Succeeded, Describe(result));
        Assert.AreEqual(MediaPackageLayout.SinglePart, result.Manifest!.Layout);
        Assert.HasCount(4, result.Manifest.Files);
    }

    [TestMethod]
    public void RootLevelFilesMustCarryTheCidKeyAndNotOnlyANullValue()
    {
        foreach (var index in new[] { 0, 1, 2, 3 })
        {
            var bytes = Mutate("single", root => Files(root)[index]!.AsObject().Remove("cid"));
            AssertRefused(bytes, InvalidManifest);
        }
    }

    [TestMethod]
    public void UnknownKeysAreRefusedAtEveryNestingLevel()
    {
        AssertRefused(
            Mutate("single", root => root["raw"] = "not part of the frozen shape"),
            InvalidManifest);
        AssertRefused(
            Mutate("single", root => SelectedParts(root)[0]!.AsObject()["raw"] = "extra"),
            InvalidManifest);
        AssertRefused(
            Mutate("single", root => FileAt(root, 0)["raw"] = "extra"),
            InvalidManifest);
    }

    [TestMethod]
    public void SinglePackageLayoutTemplateIsExact()
    {
        AssertRefused(
            Mutate("single", root => FileAt(root, 3)["path"] = "Season 01/S01E01-cid-101.mp4"),
            "invalid_file_set");
        AssertRefused(
            Mutate("single", root => FileAt(root, 3)["path"] = "video.mkv"),
            "invalid_file_set");
        AssertRefused(
            Mutate("single", root => FileAt(root, 0)["path"] = "tvshow.nfo"),
            "invalid_file_set");
        AssertRefused(
            Mutate("single", root => FileAt(root, 2)["path"] = "extra.json"),
            "invalid_file_set");
        AssertRefused(
            Mutate("single", root => FileAt(root, 1)["path"] = "art.png"),
            "invalid_file_set");
        AssertRefused(
            Mutate("single", root => FileAt(root, 3)["path"] = "video.mp4x"),
            "invalid_file_set");
    }

    [TestMethod]
    public void SinglePackageAcceptsBothFrozenImageAndVideoExtensions()
    {
        Assert.IsTrue(
            ReadWithReader(Mutate("single", root => FileAt(root, 1)["path"] = "poster.jpg")).Succeeded);
        Assert.IsTrue(
            ReadWithReader(Mutate("single", root =>
            {
                root["media_extension"] = "mkv";
                FileAt(root, 3)["path"] = "video.mkv";
            })).Succeeded);
    }

    [TestMethod]
    public void MultipartCidPrefixAndEpisodeMappingAreExact()
    {
        // A cid that is only a prefix of the declared one is not the declared one.
        AssertRefused(
            Mutate("multipart", root => FileAt(root, 0)["path"] = "Season 01/S01E01-cid-10.mp4"),
            "invalid_file_set");

        // A file whose episode number disagrees with its own selected part is not that part's file.
        AssertRefused(
            Mutate("multipart", root => FileAt(root, 0)["path"] = "Season 01/S01E02-cid-101.mp4"),
            "invalid_file_set");

        // A single-digit episode number is not the frozen two-digit spelling.
        AssertRefused(
            Mutate("multipart", root => FileAt(root, 0)["path"] = "Season 01/S01E1-cid-101.mp4"),
            "invalid_file_set");

        // A cid that is not selected at all is an unselected object.
        AssertRefused(
            Mutate("multipart", root => FileAt(root, 0)["path"] = "Season 01/S01E01-cid-999.mp4"),
            "invalid_file_set");
    }

    [TestMethod]
    public void MultipartEpisodeDirectoryIsTheFrozenSeasonDirectory()
    {
        // The frozen layout pins "Season 01/": a package whose episode entries all agree on a different
        // season directory is not the frozen layout. Moving every entry keeps the set internally consistent,
        // which is exactly why the directory cannot be derived from the input.
        AssertRefused(MoveEveryEpisodeEntry("Season 01/", "Season 99/"), "invalid_file_set");

        // Dropping the season directory entirely is not the frozen layout either: the episode files are not
        // root files.
        AssertRefused(MoveEveryEpisodeEntry("Season 01/", string.Empty), "invalid_file_set");

        // A self-chosen subdirectory is refused for the same reason.
        AssertRefused(MoveEveryEpisodeEntry("Season 01/", "Season 01/Extras/"), "invalid_file_set");

        // The frozen spelling itself still reads, so the three refusals above are about the directory and
        // not about the files.
        Assert.IsTrue(ReadWithReader(Mutate("multipart", _ => { })).Succeeded);
    }

    /// <summary>
    /// Rewrites every episode entry of the multipart example from one directory prefix to another, leaving
    /// the root-level entries untouched, so the mutated package stays internally consistent.
    /// </summary>
    private static byte[] MoveEveryEpisodeEntry(string from, string to) =>
        Mutate(
            "multipart",
            root =>
            {
                foreach (var file in Files(root))
                {
                    var path = file!["path"]!.GetValue<string>();
                    if (path.StartsWith(from, StringComparison.Ordinal))
                    {
                        file["path"] = to + path[from.Length..];
                    }
                }
            });

    [TestMethod]
    public void EscapedSurrogateHalvesAreRefusedAsAManifestVerdict()
    {
        // A JSON escape can spell half of a UTF-16 surrogate pair. That is legal JSON syntax, and the
        // parser accepts it, but decoding the string fails, so the input must be refused as a named
        // manifest verdict instead of letting the failure escape as a framework exception. The three
        // cases below cover a string value, a file path and a property name.
        foreach (var rawEscape in new[] { "\\uD800", "\\uDC00", "\\uD800XDC00" })
        {
            AssertRefusedWithNamedVerdict(
                Mutate("single", root => root["bvid"] = "BV0000000001" + rawEscape),
                "a bvid value carrying " + rawEscape);
            AssertRefusedWithNamedVerdict(
                Mutate("single", root => FileAt(root, 3)["path"] = "video" + rawEscape + ".mp4"),
                "a file path carrying " + rawEscape);
            AssertRefusedWithNamedVerdict(
                Mutate("single", root => RenameRootKey(root, "provider", "provi" + rawEscape + "der")),
                "a property name carrying " + rawEscape);
        }
    }

    [TestMethod]
    public void LegalEscapePairsAndTheirUtf8SpellingAreTreatedAlike()
    {
        // A legal surrogate pair is well-formed text, so the syntax guard must accept it: the same
        // character written as UTF-8 and written as a pair of escapes has to behave identically. The
        // character is not allowed in these fields, so identity validation refuses both with the same
        // frozen code, which is where field semantics belong.
        const string SmilingFace = "\uD83D\uDE00";
        var escaped = Mutate("single", root => root["bvid"] = "BV0000000001" + "\\uD83D\\uDE00");
        var rawUtf8 = Mutate("single", root => root["bvid"] = "BV0000000001" + SmilingFace);

        AssertRefused(escaped, "invalid_identity");
        AssertRefused(rawUtf8, "invalid_identity");
        Assert.AreEqual(
            Describe(ReadWithReader(rawUtf8)),
            Describe(ReadWithReader(escaped)),
            "The same character must not be judged differently depending on how it is spelled.");

        // An escaped backslash followed by the letter u is an ordinary string, not an escape sequence, so
        // it is not a decode failure either.
        AssertRefused(
            Mutate("single", root => root["bvid"] = "BV0000000001" + "\\\\uD800"),
            "invalid_identity");
    }

    /// <summary>
    /// Asserts that the manifest is refused and that the refusal is a named result: the reader reports a
    /// frozen code, and no framework exception reaches the caller.
    /// </summary>
    private static void AssertRefusedWithNamedVerdict(byte[] manifestBytes, string what)
    {
        MediaPackageManifestReadResult result;
        try
        {
            result = ReadWithReader(manifestBytes);
        }
        catch (Exception exception)
        {
            Assert.Fail($"Reading {what} threw {exception.GetType().Name} instead of returning a verdict.");
            return;
        }

        Assert.IsNull(result.Manifest, $"Reading {what} must be refused.");
        Assert.IsTrue(
            result.FailureCode is not null || result.Issues.Count > 0,
            $"Reading {what} must report a named code.");
        Assert.IsTrue(
            Codes(result).All(code => FrozenCodes.Contains(code, StringComparer.Ordinal)),
            Describe(result));
    }

    private static IReadOnlyList<string> Codes(MediaPackageManifestReadResult result) =>
        result.FailureCode is null
            ? [.. result.Issues.Select(issue => issue.Code)]
            : [result.FailureCode];

    /// <summary>
    /// Replaces one root-level property name, so a mutated key name can be written through the same
    /// escaping rules as any other string.
    /// </summary>
    private static void RenameRootKey(JsonObject root, string from, string to)
    {
        var value = root[from]!.DeepClone();
        Assert.IsTrue(root.Remove(from));
        root[to] = value;
    }

    [TestMethod]
    public void MultipartRequiredObjectsAndPerCidThumbCountAreExact()
    {
        // The show nfo is required exactly once: dropping it leaves the layout incomplete.
        AssertRefused(
            Mutate("multipart", root => Files(root).RemoveAt(Files(root).Count - 1)),
            "invalid_file_set");

        // One episode may carry at most one thumb.
        AssertRefused(
            Mutate("multipart", root => Files(root).Add(new JsonObject
            {
                ["path"] = "Season 01/S01E02-cid-202-thumb.jpg",
                ["kind"] = "episode_thumb",
                ["cid"] = "202",
                ["size_bytes"] = 63,
                ["sha256"] = new string('0', 64),
            })),
            "invalid_file_set");

        // A root-level poster is optional, but two of them are not.
        AssertRefused(
            Mutate("multipart", root => Files(root).Add(new JsonObject
            {
                ["path"] = "poster.jpg",
                ["kind"] = "poster",
                ["cid"] = null,
                ["size_bytes"] = 39,
                ["sha256"] = new string('1', 64),
            })),
            "invalid_file_set");
    }

    [TestMethod]
    public void DuplicateAndExtraObjectsAreRefusedWithTheirFrozenCodes()
    {
        AssertRefused(
            Mutate("single", root => Files(root).Add(new JsonObject
            {
                ["path"] = "Movie.NFO",
                ["kind"] = "nfo",
                ["cid"] = null,
                ["size_bytes"] = 605,
                ["sha256"] = new string('2', 64),
            })),
            "duplicate_path");

        AssertRefused(
            Mutate("single", root => Files(root).Add(new JsonObject
            {
                ["path"] = "extra.txt",
                ["kind"] = "source",
                ["cid"] = null,
                ["size_bytes"] = 1,
                ["sha256"] = new string('3', 64),
            })),
            "invalid_file_set");
    }

    [TestMethod]
    public void RawPathSpellingIsRefusedBeforeAnyNormalization()
    {
        foreach (var spelling in new[]
        {
            "../movie.nfo",
            "Season 01/../../video.mp4",
            "dir\\movie.nfo",
            "movie.nfo:payload",
            "C:/movie.nfo",
            "/movie.nfo",
            "Season 01//video.mp4",
        })
        {
            AssertRefused(
                Mutate("single", root => FileAt(root, 0)["path"] = spelling),
                "invalid_path");
        }
    }

    [TestMethod]
    public void ValidatedManifestCollectionsAreDefensiveSnapshots()
    {
        var result = ReadWithReader(Mutate("single", root => Files(root)[0]!["path"] = "movie.nfo"));
        Assert.IsTrue(result.Succeeded, Describe(result));
        var manifest = result.Manifest!;
        var files = (IList<MediaPackageFileEntry>)manifest.Files;
        var parts = (IList<MediaPackageSelectedPart>)manifest.SelectedParts;

        Assert.ThrowsExactly<NotSupportedException>(
            () => files.Add(new MediaPackageFileEntry(
                "poster.jpg",
                MediaPackageFileKind.Poster,
                null,
                1,
                new Sha256Digest(new string('4', 64)))));
        Assert.ThrowsExactly<NotSupportedException>(
            () => parts.Add(new MediaPackageSelectedPart("303", null)));
        Assert.HasCount(4, manifest.Files);
        Assert.HasCount(1, manifest.SelectedParts);
    }

    private static byte[] NormalizeLineEndings(byte[] bytes) =>
        bytes.AsSpan().IndexOf("\r\n"u8) < 0
            ? bytes
            : Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal));

    private static readonly JsonWriterOptions CompactWriter = new()
    {
        Indented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Rebuilds one example manifest with a named mutation applied, so a shape, identity or layout
    /// refusal is expressed as the single change under test rather than as a hand-written document.
    /// </summary>
    private static byte[] Mutate(string exampleName, Action<JsonObject> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var example = MediaPackageSandbox.ReadExamples().Single(item => item.Name == exampleName);
        var root = (JsonObject)JsonNode.Parse(example.ManifestBytes)!;
        mutate(root);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CompactWriter))
        {
            root.WriteTo(writer);
        }

        return stream.ToArray();
    }

    private static MediaPackageManifestReadResult ReadWithReader(byte[] bytes) =>
        new MediaPackageManifestReader(new MediaPackageInspectionLimits())
            .Read(bytes, new Sha256Digest(Convert.ToHexStringLower(SHA256.HashData(bytes))));

    private static JsonObject FileAt(JsonObject root, int index) =>
        (JsonObject)root["files"]!.AsArray()[index]!;

    private static JsonArray SelectedParts(JsonObject root) => root["selected_parts"]!.AsArray();

    private static JsonArray Files(JsonObject root) => root["files"]!.AsArray();

    /// <summary>
    /// Asserts that the mutated manifest is refused and that the expected frozen code is among the
    /// reported ones.
    /// </summary>
    private static void AssertRefused(byte[] manifestBytes, string expectedCode)
    {
        var result = ReadWithReader(manifestBytes);
        Assert.IsNull(result.Manifest, $"The manifest must be refused with {expectedCode}.");
        var codes = result.FailureCode is null
            ? result.Issues.Select(issue => issue.Code).ToArray()
            : [result.FailureCode];
        CollectionAssert.Contains(codes, expectedCode, Describe(result));
    }

    private static string Describe(MediaPackageManifestReadResult result) =>
        "failure=" + (result.FailureCode ?? "-")
        + " issues=[" + string.Join(", ", result.Issues.Select(issue => issue.Code + "@" + (issue.Location ?? "-")))
        + "]";

    /// <summary>
    /// Rewrites the root object with its members in reverse order while preserving every value, proving
    /// that member order is not part of the accepted shape while the raw digest still is.
    /// </summary>
    private static byte[] ReorderRootKeys(byte[] manifestBytes)
    {
        using var document = JsonDocument.Parse(manifestBytes);
        var ordered = document.RootElement.EnumerateObject().Reverse().ToArray();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in ordered)
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
