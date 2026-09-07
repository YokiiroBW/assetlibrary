using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

internal abstract record AssetLinkReadRequest(string RequestId, string Operation);

internal sealed record GetLibraryAssetLinkRequest(string RequestId, GetLibraryQuery Query)
    : AssetLinkReadRequest(RequestId, "libraries.get");

internal sealed record GetEntryAssetLinkRequest(string RequestId, GetEntryQuery Query)
    : AssetLinkReadRequest(RequestId, "entries.get");

internal sealed record ListLibrariesAssetLinkRequest(
    string RequestId,
    ListLibrariesQuery Query)
    : AssetLinkReadRequest(RequestId, "libraries.list");

internal sealed record BrowseEntriesAssetLinkRequest(
    string RequestId,
    BrowseEntriesQuery Query)
    : AssetLinkReadRequest(RequestId, "entries.browse");

internal sealed record SearchAssetsAssetLinkRequest(
    string RequestId,
    SearchAssetsQuery Query)
    : AssetLinkReadRequest(RequestId, "assets.search");

internal sealed record UnsupportedAssetLinkReadRequest(string RequestId)
    : AssetLinkReadRequest(RequestId, "unsupported");

internal sealed class AssetLinkAuthenticationException : Exception
{
}
