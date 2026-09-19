using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    private static byte[] NormalizeLineEndings(byte[] bytes) =>
        bytes.AsSpan().IndexOf("\r\n"u8) < 0
            ? bytes
            : Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal));

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
