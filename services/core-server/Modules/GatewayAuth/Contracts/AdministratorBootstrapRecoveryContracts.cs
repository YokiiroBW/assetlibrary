using System.Security.Cryptography;

namespace AssetLibrary.Modules.GatewayAuth.Contracts;

public enum AdministratorBootstrapRecoveryAction
{
    BootstrapFirstAdministrator = 0,
    RecoverAdministrator = 1,
}

public enum AdministratorBootstrapRecoveryOutcome
{
    Applied = 0,
    AuthorizationRejected = 1,
    SecretRejected = 2,
    DependencyUnavailable = 3,
    StateConflict = 4,
    RequestConflict = 5,
}

public sealed record AdministratorBootstrapRecoveryResult
{
    public AdministratorBootstrapRecoveryResult(
        AdministratorBootstrapRecoveryOutcome outcome,
        bool wasReplayed,
        LocalAccountState? account = null)
    {
        var applied = outcome == AdministratorBootstrapRecoveryOutcome.Applied;
        if (!Enum.IsDefined(outcome)
            || applied != (account is not null)
            || (applied && account is not { IsSystemAdministrator: true, IsEnabled: true }))
        {
            throw new ArgumentException("An administrator bootstrap or recovery result is invalid.");
        }

        Outcome = outcome;
        WasReplayed = wasReplayed;
        Account = account;
    }

    public AdministratorBootstrapRecoveryOutcome Outcome { get; }

    public bool WasReplayed { get; }

    public LocalAccountState? Account { get; }

    public static AdministratorBootstrapRecoveryResult Rejected(
        AdministratorBootstrapRecoveryOutcome outcome)
    {
        if (outcome == AdministratorBootstrapRecoveryOutcome.Applied)
        {
            throw new ArgumentException("An applied result requires account state.", nameof(outcome));
        }

        return new(outcome, wasReplayed: false);
    }
}

public sealed class OutOfBandAuthorizationProof : IDisposable
{
    public const int MinimumByteLength = 16;
    public const int MaximumByteLength = 1024;
    private byte[]? value;

    public OutOfBandAuthorizationProof(ReadOnlySpan<byte> value)
    {
        if (value.Length is < MinimumByteLength or > MaximumByteLength)
        {
            throw new ArgumentException("An out-of-band authorization proof is invalid.", nameof(value));
        }

        this.value = value.ToArray();
    }

    internal ReadOnlyMemory<byte> Value => value
        ?? throw new ObjectDisposedException(nameof(OutOfBandAuthorizationProof));

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref value, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public override string ToString() => "[redacted]";
}
