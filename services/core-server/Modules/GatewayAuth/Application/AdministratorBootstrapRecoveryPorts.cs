using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal sealed record OutOfBandAuthorizationRequest
{
    public OutOfBandAuthorizationRequest(
        AdministratorBootstrapRecoveryAction action,
        Guid operationId,
        LocalAccountName targetAccountName,
        DateTimeOffset expiresAt)
    {
        if (!Enum.IsDefined(action)
            || operationId == Guid.Empty
            || string.IsNullOrEmpty(targetAccountName.Value))
        {
            throw new ArgumentException("An out-of-band authorization request is invalid.");
        }

        Action = action;
        OperationId = operationId;
        TargetAccountName = targetAccountName;
        ExpiresAt = expiresAt.ToUniversalTime();
    }

    public AdministratorBootstrapRecoveryAction Action { get; }

    public Guid OperationId { get; }

    public LocalAccountName TargetAccountName { get; }

    public DateTimeOffset ExpiresAt { get; }
}

internal sealed record VerifiedOutOfBandAuthorization
{
    public VerifiedOutOfBandAuthorization(
        Guid authorizationId,
        AdministratorBootstrapRecoveryAction action,
        Guid operationId,
        LocalAccountName targetAccountName,
        DateTimeOffset expiresAt)
    {
        if (authorizationId == Guid.Empty
            || !Enum.IsDefined(action)
            || operationId == Guid.Empty
            || string.IsNullOrEmpty(targetAccountName.Value))
        {
            throw new ArgumentException("A verified out-of-band authorization is invalid.");
        }

        AuthorizationId = authorizationId;
        Action = action;
        OperationId = operationId;
        TargetAccountName = targetAccountName;
        ExpiresAt = expiresAt.ToUniversalTime();
    }

    public Guid AuthorizationId { get; }

    public AdministratorBootstrapRecoveryAction Action { get; }

    public Guid OperationId { get; }

    public LocalAccountName TargetAccountName { get; }

    public DateTimeOffset ExpiresAt { get; }

    public bool Matches(OutOfBandAuthorizationRequest request) =>
        request is not null
        && Action == request.Action
        && OperationId == request.OperationId
        && TargetAccountName == request.TargetAccountName
        && ExpiresAt == request.ExpiresAt;
}

internal enum OutOfBandAuthorizationVerificationStatus
{
    Verified = 0,
    Rejected = 1,
    Expired = 2,
    Unavailable = 3,
}

internal sealed record OutOfBandAuthorizationVerificationResult(
    OutOfBandAuthorizationVerificationStatus Status,
    VerifiedOutOfBandAuthorization? Authorization = null)
{
    public static OutOfBandAuthorizationVerificationResult Verified(
        VerifiedOutOfBandAuthorization authorization) =>
        new(
            OutOfBandAuthorizationVerificationStatus.Verified,
            authorization ?? throw new ArgumentNullException(nameof(authorization)));

    public static OutOfBandAuthorizationVerificationResult Rejected() =>
        new(OutOfBandAuthorizationVerificationStatus.Rejected);

    public static OutOfBandAuthorizationVerificationResult Expired() =>
        new(OutOfBandAuthorizationVerificationStatus.Expired);

    public static OutOfBandAuthorizationVerificationResult Unavailable() =>
        new(OutOfBandAuthorizationVerificationStatus.Unavailable);
}

internal interface IOutOfBandAuthorizationVerifier
{
    ValueTask<OutOfBandAuthorizationVerificationResult> VerifyAsync(
        OutOfBandAuthorizationRequest request,
        OutOfBandAuthorizationProof proof,
        CancellationToken cancellationToken);
}

internal interface IAdministratorBootstrapRecoveryStore
{
    ValueTask<AdministratorBootstrapRecoveryResult> BootstrapAsync(
        VerifiedOutOfBandAuthorization authorization,
        Guid requestedPrincipalId,
        LocalAccountDisplayName displayName,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken);

    ValueTask<AdministratorBootstrapRecoveryResult> RecoverAsync(
        VerifiedOutOfBandAuthorization authorization,
        long expectedCredentialVersion,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken);
}
