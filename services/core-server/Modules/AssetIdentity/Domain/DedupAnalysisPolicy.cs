using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Domain;

/// <summary>
/// Pure grouping rules for the read-only preview. It encodes the product rule that only a complete
/// strong hash proves a byte duplicate, and that equal names or equal sizes prove nothing at all.
/// </summary>
public static class DedupAnalysisPolicy
{
    /// <summary>
    /// Statistical limit for relationship detection. A directory with thousands of identically
    /// named files cannot turn a preview into a quadratic scan, so relationship evidence is capped
    /// per bucket instead of silently growing with the library.
    /// </summary>
    public const int MaximumRelationCandidatesPerBucket = 64;

    private static readonly HashSet<string> CompanionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".txt", ".xmp", ".xml", ".yaml", ".yml", ".md", ".srt", ".vtt", ".nfo", ".url",
    };

    private static readonly Dictionary<string, string> AnimatedContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ".apng",
        [".apng"] = ".png",
        [".gif"] = ".png",
        [".webp"] = ".gif",
        [".jpg"] = ".mp4",
        [".jpeg"] = ".mp4",
    };

    private static readonly Dictionary<string, string> EncodingPeers = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ".png",
        [".jpeg"] = ".png",
        [".heic"] = ".jpg",
        [".tif"] = ".png",
        [".tiff"] = ".png",
        [".bmp"] = ".png",
    };

    /// <summary>
    /// Length buckets are the only pre-filter. Same length is never evidence, it merely decides
    /// which files could still share content, so a 500k asset library costs one pass plus one
    /// strong hash per real candidate instead of one comparison per pair.
    /// </summary>
    public static IReadOnlyDictionary<long, int> CountLengthBuckets(IReadOnlyList<DedupAnalysisEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var buckets = new Dictionary<long, int>();
        foreach (var entry in entries)
        {
            if (!IsContentCandidate(entry))
            {
                continue;
            }

            buckets[entry.Length] = buckets.GetValueOrDefault(entry.Length) + 1;
        }

        return buckets;
    }

    public static bool IsContentCandidate(DedupAnalysisEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return !entry.IsReparsePoint && !entry.IsExcluded;
    }

    public static DedupSkipReason SkipReasonFor(DedupAnalysisEntry entry, IReadOnlyDictionary<long, int> buckets)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(buckets);
        if (entry.IsReparsePoint)
        {
            return DedupSkipReason.ReparsePoint;
        }

        if (entry.IsExcluded)
        {
            return DedupSkipReason.ExcludedByDiscoveryPolicy;
        }

        return buckets.GetValueOrDefault(entry.Length) < 2
            ? DedupSkipReason.NoLengthCandidate
            : DedupSkipReason.None;
    }

    /// <summary>
    /// A byte duplicate group requires identical length and identical complete strong hash for
    /// every member. Membership never merges asset identity and never implies a keeper.
    /// </summary>
    public static IReadOnlyList<DedupPlanGroup> BuildGroups(IReadOnlyList<DedupAnalysisEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var groups = new List<DedupPlanGroup>();
        var byContent = entries
            .Where(entry => entry.ReadState == DedupItemReadState.ContentVerified && entry.Signature is not null)
            .GroupBy(entry => BuildGroupKey(entry.Signature!), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in byContent)
        {
            var members = group
                .OrderBy(entry => entry.SourceIdentity, StringComparer.Ordinal)
                .ToArray();
            if (members.Length < 2)
            {
                continue;
            }

            groups.Add(new DedupPlanGroup(
                group.Key,
                members[0].Signature!.Length,
                members.Select(entry => entry.SourceIdentity).ToArray(),
                AssetRelation.ByteDuplicate,
                IdentityMergeProposed: false));
        }

        return groups;
    }

    public static string BuildGroupKey(ContentSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return $"sha256:{signature.Sha256.Value}:{signature.Length}";
    }

    /// <summary>
    /// Relationships that must stay visible as separate assets. A companion file, an animation, a
    /// re-encode, a revision or a same-length neighbour is reported as a relation, never as a
    /// duplicate to collapse.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<AssetRelation>> DetectRelations(
        IReadOnlyList<DedupAnalysisEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Two indices keep the cost linear in the number of files: one by base name for the
        // name-shaped relations, one by content length for "same size, different bytes". No pair of
        // the whole library is ever compared.
        var relations = new Dictionary<string, SortedSet<AssetRelation>>(StringComparer.Ordinal);
        var byBaseName = new Dictionary<string, List<DedupAnalysisEntry>>(StringComparer.Ordinal);
        var byLength = new Dictionary<long, List<DedupAnalysisEntry>>();
        foreach (var entry in entries)
        {
            if (entry.IsReparsePoint || entry.IsExcluded)
            {
                continue;
            }

            Add(byBaseName, BaseName(entry.RelativePath.Value), entry);
            Add(byLength, entry.Length, entry);
        }

        foreach (var bucket in byBaseName.Values)
        {
            if (bucket.Count > 1)
            {
                Compare(bucket, relations);
            }
        }

        foreach (var bucket in byLength.Values)
        {
            if (bucket.Count > 1)
            {
                CompareSameLength(bucket, relations);
            }
        }

        return relations.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<AssetRelation>)pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    public static DedupCategory Categorize(
        DedupAnalysisEntry entry,
        IReadOnlyList<AssetRelation>? relations)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.ReadState == DedupItemReadState.ReadFailed)
        {
            return DedupCategory.Unreadable;
        }

        // A companion, animation, re-encode or same-name neighbour is never presented as a plain
        // unique asset, even when its own content was read successfully.
        if (relations is not null && relations.Count > 0)
        {
            if (relations.Contains(AssetRelation.CompanionFile)
                || relations.Contains(AssetRelation.AnimatedVariant)
                || relations.Contains(AssetRelation.EncodingVariant)
                || relations.Contains(AssetRelation.RevisionVariant))
            {
                return DedupCategory.CompanionOrVariant;
            }

            if (relations.Contains(AssetRelation.SameNameDifferentContent))
            {
                return DedupCategory.SameNameDifferentContent;
            }

            if (relations.Contains(AssetRelation.SameLengthDifferentContent))
            {
                return DedupCategory.SameLengthDifferentContent;
            }
        }

        return DedupCategory.Unique;
    }

    private static void CompareSameLength(
        List<DedupAnalysisEntry> bucket,
        Dictionary<string, SortedSet<AssetRelation>> relations)
    {
        for (var left = 0; left < bucket.Count; left++)
        {
            for (var right = left + 1; right < bucket.Count; right++)
            {
                var relation = ClassifySameLength(bucket[left], bucket[right]);
                if (relation == AssetRelation.None)
                {
                    continue;
                }

                Add(relations, bucket[left].SourceIdentity, relation);
                Add(relations, bucket[right].SourceIdentity, relation);
            }
        }
    }

    /// <summary>
    /// Equal size with different content is recorded so a reader sees that size was checked and
    /// rejected. Equal hashes are already a byte duplicate group and are not repeated here.
    /// </summary>
    private static AssetRelation ClassifySameLength(DedupAnalysisEntry left, DedupAnalysisEntry right)
    {
        var leftHash = left.Signature?.Sha256.Value;
        var rightHash = right.Signature?.Sha256.Value;
        if (leftHash is not null && rightHash is not null
            && string.Equals(leftHash, rightHash, StringComparison.Ordinal))
        {
            return AssetRelation.None;
        }

        return BaseName(left.RelativePath.Value) == BaseName(right.RelativePath.Value)
            ? AssetRelation.SameNameDifferentContent
            : AssetRelation.SameLengthDifferentContent;
    }

    private static void Add<TKey>(
        Dictionary<TKey, List<DedupAnalysisEntry>> index,
        TKey key,
        DedupAnalysisEntry entry)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var bucket))
        {
            bucket = [];
            index[key] = bucket;
        }

        if (bucket.Count < MaximumRelationCandidatesPerBucket)
        {
            bucket.Add(entry);
        }
    }

    private static void Compare(
        List<DedupAnalysisEntry> bucket,
        Dictionary<string, SortedSet<AssetRelation>> relations)
    {
        for (var left = 0; left < bucket.Count; left++)
        {
            for (var right = left + 1; right < bucket.Count; right++)
            {
                var relation = Classify(bucket[left], bucket[right]);
                if (relation == AssetRelation.None)
                {
                    continue;
                }

                Add(relations, bucket[left].SourceIdentity, relation);
                Add(relations, bucket[right].SourceIdentity, relation);
            }
        }
    }

    private static AssetRelation Classify(DedupAnalysisEntry left, DedupAnalysisEntry right)
    {
        var leftExtension = Extension(left.RelativePath.Value);
        var rightExtension = Extension(right.RelativePath.Value);
        if (CompanionExtensions.Contains(leftExtension) || CompanionExtensions.Contains(rightExtension))
        {
            return AssetRelation.CompanionFile;
        }

        if (Matches(AnimatedContainers, leftExtension, rightExtension))
        {
            return AssetRelation.AnimatedVariant;
        }

        if (Matches(EncodingPeers, leftExtension, rightExtension))
        {
            return AssetRelation.EncodingVariant;
        }

        if (string.Equals(leftExtension, rightExtension, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                left.RelativePath.Value,
                right.RelativePath.Value,
                StringComparison.Ordinal))
        {
            return SameNameRelation(left, right);
        }

        // Two same-size neighbours of different kinds are still worth naming, so the preview shows
        // that the size was compared and rejected instead of staying silent about it.
        return ClassifySameLength(left, right);
    }

    private static AssetRelation SameNameRelation(DedupAnalysisEntry left, DedupAnalysisEntry right)
    {
        if (left.Length != right.Length)
        {
            return AssetRelation.None;
        }

        var leftHash = left.Signature?.Sha256.Value;
        var rightHash = right.Signature?.Sha256.Value;
        if (leftHash is not null && rightHash is not null)
        {
            return string.Equals(leftHash, rightHash, StringComparison.Ordinal)
                ? AssetRelation.ByteDuplicate
                : AssetRelation.SameNameDifferentContent;
        }

        return AssetRelation.RevisionVariant;
    }

    private static bool Matches(
        Dictionary<string, string> map,
        string leftExtension,
        string rightExtension) =>
        (map.TryGetValue(leftExtension, out var expected)
            && string.Equals(expected, rightExtension, StringComparison.OrdinalIgnoreCase))
        || (map.TryGetValue(rightExtension, out var mirrored)
            && string.Equals(mirrored, leftExtension, StringComparison.OrdinalIgnoreCase));

    private static void Add(
        Dictionary<string, SortedSet<AssetRelation>> relations,
        string identity,
        AssetRelation relation)
    {
        if (!relations.TryGetValue(identity, out var set))
        {
            set = [];
            relations[identity] = set;
        }

        set.Add(relation);
    }

    private static string BaseName(string relativePath)
    {
        var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        var extension = Extension(relativePath);
        return extension.Length == 0 ? name : name[..^extension.Length];
    }

    private static string Extension(string relativePath)
    {
        var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? string.Empty : name[dot..];
    }
}
