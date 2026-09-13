namespace AssetLibrary.Windows.AssetHost;

// One admission object is shared by both production endpoints for the complete Host lifetime.
internal sealed class ImageClientCapacity
{
    private int active;
    internal int Active => Volatile.Read(ref active);
    internal bool TryEnter()
    {
        var observed = Active;
        while (observed < 4)
        {
            var previous = Interlocked.CompareExchange(ref active, observed + 1, observed);
            if (previous == observed) { return true; }
            observed = previous;
        }
        return false;
    }
    internal void Exit() => Interlocked.Decrement(ref active);
}
