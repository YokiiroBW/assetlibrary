namespace AssetLibrary.Modules.PreviewProvider.Application;

internal sealed class ImagePreviewCache
{
    private const int MaximumEntries = 256;
    private const int MaximumBytes = 64 * 1024 * 1024;
    private readonly object guard = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> recent = [];
    private long bytes;

    public byte[]? Find(string key)
    {
        lock (guard)
        {
            if (!entries.TryGetValue(key, out var node)) return null;
            recent.Remove(node);
            recent.AddFirst(node);
            return node.Value.Png;
        }
    }

    public void Store(string key, byte[] png)
    {
        if (png.Length > MaximumBytes) return;
        lock (guard)
        {
            if (entries.ContainsKey(key)) return;
            while (entries.Count >= MaximumEntries || bytes + png.Length > MaximumBytes)
            {
                var oldest = recent.Last!;
                recent.RemoveLast();
                entries.Remove(oldest.Value.Key);
                bytes -= oldest.Value.Png.Length;
            }
            entries.Add(key, recent.AddFirst(new CacheEntry(key, png)));
            bytes += png.Length;
        }
    }

    private sealed record CacheEntry(string Key, byte[] Png);
}
