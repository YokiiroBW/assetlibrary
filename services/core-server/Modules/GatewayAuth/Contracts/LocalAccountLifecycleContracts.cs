namespace AssetLibrary.Modules.GatewayAuth.Contracts;

public readonly record struct LocalAccountDisplayName
{
    public const int MaximumLength = 200;

    public LocalAccountDisplayName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > MaximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("A local account display name is invalid.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record LocalAccountState
{
    public LocalAccountState(
        Guid principalId,
        LocalAccountName accountName,
        LocalAccountDisplayName displayName,
        bool isSystemAdministrator,
        bool isEnabled,
        long credentialVersion,
        long principalSessionVersion)
    {
        if (principalId == Guid.Empty
            || credentialVersion <= 0
            || principalSessionVersion <= 0)
        {
            throw new ArgumentException("A local account state is invalid.");
        }

        PrincipalId = principalId;
        AccountName = accountName;
        DisplayName = displayName;
        IsSystemAdministrator = isSystemAdministrator;
        IsEnabled = isEnabled;
        CredentialVersion = credentialVersion;
        PrincipalSessionVersion = principalSessionVersion;
    }

    public Guid PrincipalId { get; }

    public LocalAccountName AccountName { get; }

    public LocalAccountDisplayName DisplayName { get; }

    public bool IsSystemAdministrator { get; }

    public bool IsEnabled { get; }

    public long CredentialVersion { get; }

    public long PrincipalSessionVersion { get; }
}

public enum LocalAccountLifecycleOutcome
{
    Applied = 0,
    Unauthorized = 1,
    SecretRejected = 2,
    DependencyUnavailable = 3,
    AccountConflict = 4,
    NotFound = 5,
    StateConflict = 6,
    LastAdministrator = 7,
    OperationConflict = 8,
}

public sealed record LocalAccountLifecycleResult(
    LocalAccountLifecycleOutcome Outcome,
    bool WasReplayed,
    LocalAccountState? Account = null)
{
    public static LocalAccountLifecycleResult Rejected(LocalAccountLifecycleOutcome outcome)
    {
        if (outcome == LocalAccountLifecycleOutcome.Applied)
        {
            throw new ArgumentException("An applied lifecycle result requires account state.", nameof(outcome));
        }

        return new(outcome, WasReplayed: false);
    }
}
