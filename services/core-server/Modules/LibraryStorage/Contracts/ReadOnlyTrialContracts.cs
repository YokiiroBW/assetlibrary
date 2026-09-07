namespace AssetLibrary.Modules.LibraryStorage.Contracts;

public sealed record ConfiguredStorageSource(
    StorageSourceId StorageSourceId,
    string SourceKey,
    string DisplayName,
    CanonicalLibraryRoot AllowedRoot);

public readonly record struct ManagementOperation(Guid PrincipalId, Guid IdempotencyKey)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfEqual(PrincipalId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(IdempotencyKey, Guid.Empty);
    }
}

public sealed record LibraryRegistrationRequest(string SourceKey, string DisplayName, string RootPath);

public sealed class ReadOnlyTrialException(string code) : InvalidOperationException("The read-only trial operation was rejected.")
{
    public string Code { get; } = code;
}

public interface ILibraryRegistration
{
    ValueTask<LibraryId> RegisterAsync(
        LibraryRegistrationRequest request,
        ManagementOperation operation,
        CancellationToken cancellationToken);
}

public interface ILibraryAvailability
{
    ValueTask<StorageAvailability> RefreshAsync(LibraryId libraryId, CancellationToken cancellationToken);

    ValueTask<int> RefreshBatchAsync(CancellationToken cancellationToken);
}

public sealed class ReadOnlyWorkerProcessOptions
{
    public ReadOnlyWorkerProcessOptions(
        string executablePath,
        IReadOnlyList<string> prefixArguments,
        TimeSpan? probeTimeout = null,
        TimeSpan? scanInactivityTimeout = null,
        TimeSpan? terminationTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(prefixArguments);
        ExecutablePath = executablePath;
        PrefixArguments = prefixArguments.ToArray();
        ProbeTimeout = probeTimeout ?? TimeSpan.FromSeconds(10);
        ScanInactivityTimeout = scanInactivityTimeout ?? TimeSpan.FromMinutes(1);
        TerminationTimeout = terminationTimeout ?? TimeSpan.FromSeconds(5);
        foreach (var timeout in new[] { ProbeTimeout, ScanInactivityTimeout, TerminationTimeout })
        {
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            {
                throw new ArgumentOutOfRangeException(nameof(probeTimeout));
            }
        }
    }

    public string ExecutablePath { get; }
    public IReadOnlyList<string> PrefixArguments { get; }
    public TimeSpan ProbeTimeout { get; }
    public TimeSpan ScanInactivityTimeout { get; }
    public TimeSpan TerminationTimeout { get; }
}
