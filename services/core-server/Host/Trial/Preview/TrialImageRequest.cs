using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.CoreServer.Hosting.Trial.Preview;

internal sealed record TrialImageRequest(LibraryId Library, StableEntryId Entry, ImagePreviewVariant Variant)
{
    public GetEntryQuery Authorize(AuthenticatedSubject subject) => new(subject, Library, Entry, ReadPageOptions.DefaultTimeout);

    public static TrialImageRequest? Parse(HttpContext context, string libraryText, string entryText)
    {
        if (!Guid.TryParseExact(libraryText, "D", out var libraryId) || libraryId == Guid.Empty
            || !Guid.TryParseExact(entryText, "D", out var entryId) || entryId == Guid.Empty
            || context.Request.Query.Count != 1 || !context.Request.Query.TryGetValue("variant", out var values)
            || values.Count != 1 || values[0] is not ("thumbnail" or "preview")) return null;
        return new TrialImageRequest(new LibraryId(libraryId), new StableEntryId(entryId),
            values[0] == "thumbnail" ? ImagePreviewVariant.Thumbnail : ImagePreviewVariant.Preview);
    }
}
