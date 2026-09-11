using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

internal sealed record PageLocation(WorkspaceLocation Workspace, string? Cursor);
internal sealed record ProjectedItem(SnapshotKind Kind, string Name, PageLocation? Location);

internal static class SnapshotProjection
{
    internal static async Task<List<ProjectedItem>> ReadAsync(ReadOnlyClient client, PageLocation location, CancellationToken token)
    {
        var result = new List<ProjectedItem>();
        string? next;
        if (location.Workspace.IsHome)
        {
            var page = await client.LibrariesAsync(location.Cursor, null, token).ConfigureAwait(false);
            foreach (var item in page.Items)
            { result.Add(new ProjectedItem(SnapshotKind.Library, item.Name, new PageLocation(new WorkspaceLocation(item), null))); }
            next = page.NextCursor;
        }
        else
        {
            var page = await client.EntriesAsync(location.Workspace, location.Cursor, token).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                var kind = item.Entry.Kind switch
                {
                    "directory" => SnapshotKind.Directory,
                    "reparse_file" or "reparse_directory" => SnapshotKind.Reparse,
                    _ => SnapshotKind.File,
                };
                var child = item.Entry.IsNavigable ? new PageLocation(new WorkspaceLocation(item.Library, item.Entry.RelativePath), null) : null;
                result.Add(new ProjectedItem(kind, item.Name, child));
            }
            next = page.NextCursor;
        }
        if (result.Count > ReadOnlyClient.PageSize) { throw new InvalidDataException("Invalid page size."); }
        if (next is not null) { result.Add(new ProjectedItem(SnapshotKind.NextPage, "下一页（最多 100 项）", location with { Cursor = next })); }
        foreach (var item in result) { SnapshotProtocol.ValidateName(item.Name); }
        return result;
    }
}
