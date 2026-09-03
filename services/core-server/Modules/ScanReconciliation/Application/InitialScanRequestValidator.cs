using AssetLibrary.Modules.ScanReconciliation.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

internal static class InitialScanRequestValidator
{
    private const int MaximumBatchSize = 1024;

    public static void Validate(InitialScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.LibraryId.Value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty library ID is required.", nameof(request));
        }

        if (request.Timeout <= TimeSpan.Zero || request.Timeout > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Scan timeout must be positive and bounded.");
        }

        if (request.BatchSize is <= 0 or > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Scan batch size is outside the supported range.");
        }
    }
}
