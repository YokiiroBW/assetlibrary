using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Domain;

/// <summary>
/// One entry the analyzer observed, before and after an optional content read. It is the domain's
/// working record: the application layer fills it in, the domain policies group and classify it,
/// and nothing here touches the filesystem.
/// </summary>
public sealed record DedupAnalysisEntry(
    string SourceIdentity,
    DedupSourceId SourceId,
    CanonicalLibraryRoot Root,
    RelativeAssetPath RelativePath,
    bool IsReparsePoint,
    bool IsExcluded,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    ContentSignature? Signature,
    DedupItemReadState ReadState,
    DedupReadFailure Failure,
    DedupSkipReason SkipReason);
