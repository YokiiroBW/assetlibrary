namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PwnedPasswordsPrefixRangeCache(
    int capacity,
    TimeSpan lifetime,
    TimeProvider timeProvider)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
    private long accessSequence;

    public bool TryGet(
        string prefix,
        out PwnedPasswordPrefixRange range)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(prefix, out var entry))
            {
                range = null!;
                return false;
            }

            if (timeProvider.GetElapsedTime(entry.StoredAt, timeProvider.GetTimestamp()) >= lifetime)
            {
                entries.Remove(prefix);
                range = null!;
                return false;
            }

            entry.LastAccess = ++accessSequence;
            range = entry.Range;
            return true;
        }
    }

    public void Store(
        string prefix,
        PwnedPasswordPrefixRange range)
    {
        lock (gate)
        {
            entries[prefix] = new CacheEntry(
                range,
                timeProvider.GetTimestamp(),
                ++accessSequence);
            while (entries.Count > capacity)
            {
                var oldest = entries.MinBy(static pair => pair.Value.LastAccess).Key;
                entries.Remove(oldest);
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
        }
    }

    private sealed class CacheEntry(
        PwnedPasswordPrefixRange range,
        long storedAt,
        long lastAccess)
    {
        public PwnedPasswordPrefixRange Range { get; } = range;

        public long StoredAt { get; } = storedAt;

        public long LastAccess { get; set; } = lastAccess;
    }
}
