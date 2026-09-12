using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

internal sealed class SnapshotNodeRegistry
{
    private readonly Dictionary<Guid, ProjectedItem> entries = [];
    internal int Count => entries.Count;
    internal void Clear() => entries.Clear();
    internal bool TryLocation(Guid node, out PageLocation? location)
    {
        if (node == Guid.Empty) { location = new PageLocation(new WorkspaceLocation(), null); return true; }
        location = entries.GetValueOrDefault(node)?.Location;
        return location is not null;
    }
    internal bool TryImage(Guid node, out ThumbnailTarget? target)
    {
        target = entries.GetValueOrDefault(node)?.Image;
        return target is not null;
    }
    internal List<SnapshotItem> Add(List<ProjectedItem> projection)
    {
        var items = new List<SnapshotItem>(projection.Count);
        foreach (var entry in projection)
        {
            var node = Guid.NewGuid(); entries.Add(node, entry);
            items.Add(new SnapshotItem(node, entry.Kind, entry.Name));
        }
        return items;
    }
}
