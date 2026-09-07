namespace AssetLibrary.Modules.GatewayAuth.Contracts;

public sealed record AdministratorOperatorRequest
{
    public AdministratorOperatorRequest(
        Guid authorizationId,
        Guid operationId,
        AdministratorBootstrapRecoveryAction action,
        LocalAccountName accountName,
        LocalAccountDisplayName? displayName,
        DateTimeOffset expiresAt)
    {
        if (authorizationId == Guid.Empty || operationId == Guid.Empty
            || !Enum.IsDefined(action) || string.IsNullOrEmpty(accountName.Value)
            || (action == AdministratorBootstrapRecoveryAction.BootstrapFirstAdministrator)
                != displayName.HasValue
            || (displayName.HasValue && string.IsNullOrEmpty(displayName.Value.Value)))
        {
            throw new ArgumentException("An administrator operator request is invalid.");
        }

        AuthorizationId = authorizationId;
        OperationId = operationId;
        Action = action;
        AccountName = accountName;
        DisplayName = displayName;
        ExpiresAt = expiresAt.ToUniversalTime();
    }

    public Guid AuthorizationId { get; }

    public Guid OperationId { get; }

    public AdministratorBootstrapRecoveryAction Action { get; }

    public LocalAccountName AccountName { get; }

    public LocalAccountDisplayName? DisplayName { get; }

    public DateTimeOffset ExpiresAt { get; }
}

public sealed record GatewayAuthorizationKeyState(Guid KeyId, DateTimeOffset CreatedAt);
