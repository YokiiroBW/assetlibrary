namespace AssetLibrary.Modules.GatewayAuth.Contracts;

public readonly record struct LocalAccountName
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 64;

    public LocalAccountName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < MinimumLength or > MaximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("A local account name is invalid.", nameof(value));
        }

        var normalized = value.ToLowerInvariant();
        if (!IsAsciiLetterOrDigit(normalized[0])
            || !IsAsciiLetterOrDigit(normalized[^1])
            || normalized.Any(character => !IsAccountCharacter(character)))
        {
            throw new ArgumentException("A local account name is invalid.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;

    private static bool IsAccountCharacter(char value) =>
        IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-';

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';
}

public enum PrimaryAuthenticationMethod
{
    LocalAccount = 0,
    Oidc = 1,
}

public sealed record AuthenticatedIdentity
{
    public AuthenticatedIdentity(
        Guid principalId,
        AuthenticatedSubject subject,
        string displayName,
        bool isSystemAdministrator,
        long principalSessionVersion,
        PrimaryAuthenticationMethod authenticationMethod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (principalId == Guid.Empty
            || displayName.Length > 200
            || !string.Equals(displayName, displayName.Trim(), StringComparison.Ordinal)
            || displayName.Any(char.IsControl)
            || principalSessionVersion <= 0
            || !Enum.IsDefined(authenticationMethod))
        {
            throw new ArgumentException("An authenticated identity is invalid.");
        }

        PrincipalId = principalId;
        Subject = subject;
        DisplayName = displayName;
        IsSystemAdministrator = isSystemAdministrator;
        PrincipalSessionVersion = principalSessionVersion;
        AuthenticationMethod = authenticationMethod;
    }

    public Guid PrincipalId { get; }

    public AuthenticatedSubject Subject { get; }

    public string DisplayName { get; }

    public bool IsSystemAdministrator { get; }

    public long PrincipalSessionVersion { get; }

    public PrimaryAuthenticationMethod AuthenticationMethod { get; }
}

public enum LocalSignInStatus
{
    Rejected = 0,
    Succeeded = 1,
}

public sealed record LocalSignInResult(LocalSignInStatus Status, BrowserSessionCredentials? Session)
{
    public static LocalSignInResult Rejected() => new(LocalSignInStatus.Rejected, null);

    public static LocalSignInResult Succeeded(BrowserSessionCredentials session) =>
        new(LocalSignInStatus.Succeeded, session ?? throw new ArgumentNullException(nameof(session)));
}

public sealed record SessionAuthenticationResult(AuthenticatedIdentity? Identity)
{
    public bool IsAuthenticated => Identity is not null;

    public static SessionAuthenticationResult Rejected() => new((AuthenticatedIdentity?)null);

    public static SessionAuthenticationResult Succeeded(AuthenticatedIdentity identity) =>
        new(identity ?? throw new ArgumentNullException(nameof(identity)));
}
