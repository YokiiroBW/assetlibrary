namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class PwnedPasswordsSecretRiskOptions
{
    public static readonly TimeSpan MaximumRequestTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaximumCacheLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan DefaultRequestTimeout = MaximumRequestTimeout;
    public static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromMinutes(15);
    public const int DefaultCacheCapacity = 64;
    public const int MaximumCacheCapacity = 256;
    public const int DefaultMaximumResponseBytes = 256 * 1024;
    public const int AbsoluteMaximumResponseBytes = 256 * 1024;
    public const int DefaultMinimumResponseLines = 800;
    public const int DefaultMaximumResponseLines = 5_000;
    public const int AbsoluteMaximumResponseLines = DefaultMaximumResponseLines;

    public PwnedPasswordsSecretRiskOptions(
        TimeSpan? requestTimeout = null,
        TimeSpan? cacheLifetime = null,
        int cacheCapacity = DefaultCacheCapacity,
        int maximumResponseBytes = DefaultMaximumResponseBytes,
        int minimumResponseLines = DefaultMinimumResponseLines,
        int maximumResponseLines = DefaultMaximumResponseLines)
    {
        RequestTimeout = requestTimeout ?? DefaultRequestTimeout;
        CacheLifetime = cacheLifetime ?? DefaultCacheLifetime;
        CacheCapacity = cacheCapacity;
        MaximumResponseBytes = maximumResponseBytes;
        MinimumResponseLines = minimumResponseLines;
        MaximumResponseLines = maximumResponseLines;
        Validate();
    }

    public TimeSpan RequestTimeout { get; }

    public TimeSpan CacheLifetime { get; }

    public int CacheCapacity { get; }

    public int MaximumResponseBytes { get; }

    public int MinimumResponseLines { get; }

    public int MaximumResponseLines { get; }

    private void Validate()
    {
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > MaximumRequestTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequestTimeout),
                "The Pwned Passwords request timeout is outside the safe range.");
        }

        if (CacheLifetime <= TimeSpan.Zero || CacheLifetime > MaximumCacheLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CacheLifetime),
                "The Pwned Passwords cache lifetime is outside the safe range.");
        }

        if (CacheCapacity is < 1 or > MaximumCacheCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CacheCapacity),
                "The Pwned Passwords cache capacity is outside the safe range.");
        }

        if (MaximumResponseBytes is < 1 or > AbsoluteMaximumResponseBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumResponseBytes),
                "The Pwned Passwords response byte limit is outside the safe range.");
        }

        if (MinimumResponseLines < DefaultMinimumResponseLines
            || MaximumResponseLines < MinimumResponseLines
            || MaximumResponseLines > AbsoluteMaximumResponseLines)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumResponseLines),
                "The Pwned Passwords response line limits are outside the safe range.");
        }
    }
}
